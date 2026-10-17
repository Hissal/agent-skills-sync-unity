using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Reads plugin enablement and ordered skills.config rules. Other TOML values are skipped as complete
    /// statements, including multiline arrays, inline tables and strings. Invalid relevant state fails closed.
    /// This is a config subset reader, not a general TOML parser.
    /// </summary>
    internal sealed class CodexPluginConfig
    {
        public Dictionary<string, bool?> Plugins { get; } = new Dictionary<string, bool?>(StringComparer.Ordinal);
        readonly List<SkillRule> _rules = new List<SkillRule>();

        sealed class SkillRule
        {
            public string Name;
            public string Path;
            public bool? Enabled;
            public bool Valid = true;
        }

        public bool SkillEnabled(string name, string document)
        {
            var enabled = true;
            foreach (var rule in _rules)
            {
                // Both/neither selector and empty names are ignored, not schema failures.
                if ((rule.Name == null) == (rule.Path == null)) continue;
                if (rule.Name != null ? rule.Name.Length > 0 && rule.Name == name :
                    DirectoryLink.ResolvesTo(rule.Path, document)) enabled = rule.Enabled.Value;
            }
            return enabled;
        }

        public static CodexPluginConfig Read(string path)
        {
            var config = new CodexPluginConfig();
            var result = config.Plugins;
            SkillRule rule = null;
            string plugin = null;
            var tables = new HashSet<string>(StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            string table = "";
            string text;
            try { text = File.ReadAllText(path); }
            catch (FileNotFoundException) { return config; }
            catch (DirectoryNotFoundException) { return config; }
            foreach (var statement in Statements(text))
            {
                if (statement.StartsWith("[", StringComparison.Ordinal))
                {
                    var array = statement.StartsWith("[[", StringComparison.Ordinal);
                    var count = array ? 2 : 1;
                    if (!statement.EndsWith(new string(']', count), StringComparison.Ordinal)) throw new FormatException("Invalid TOML table.");
                    var parts = Key(statement.Substring(count, statement.Length - count * 2));
                    table = string.Join("\n", parts);
                    if (!array && !tables.Add(table)) throw new FormatException("Duplicate TOML table.");
                    if (array) keys.Clear();
                    plugin = !array && parts.Count == 2 && parts[0] == "plugins" ? parts[1] : null;
                    rule = array && table == "skills\nconfig" ? new SkillRule() : null;
                    if (rule != null) config._rules.Add(rule);
                    if (!array && parts.Count >= 2 && parts[0] == "plugins" && !result.ContainsKey(parts[1]))
                        result.Add(parts[1], null);
                    continue;
                }
                var equals = Assignment(statement);
                if (equals < 0) throw new FormatException("Invalid TOML assignment.");
                var key = Key(statement.Substring(0, equals));
                if (!keys.Add(table + "\n" + string.Join("\n", key))) throw new FormatException("Duplicate TOML key.");
                var value = statement.Substring(equals + 1).Trim();
                if (value.Length == 0) throw new FormatException("Missing TOML value.");
                if (rule != null && key.Count == 1)
                {
                    if ((key[0] == "name" || key[0] == "path" || key[0] == "enabled") &&
                        !SkillValueSyntax(value))
                        throw new FormatException("Invalid TOML skill rule value.");
                    if (key[0] == "name" || key[0] == "path")
                    {
                        var selector = StringValue(value);
                        if (selector == null) rule.Valid = false;
                        else if (key[0] == "name") rule.Name = selector.Trim();
                        else rule.Path = selector;
                    }
                    if (key[0] == "enabled")
                    {
                        if (value == "true" || value == "false") rule.Enabled = value == "true";
                        else rule.Valid = false;
                    }
                }
                if (plugin == null || key.Count != 1 || key[0] != "enabled") continue;
                if (value != "true" && value != "false") throw new FormatException("Plugin enabled must be a boolean.");
                result[plugin] = value == "true";
            }
            // Upstream deserializes the user's entire skills configuration at once. A required
            // field/type failure discards all its rules, without disabling configured plugins.
            if (config._rules.Exists(r => !r.Valid || !r.Enabled.HasValue)) config._rules.Clear();
            return config;
        }

        static string StringValue(string value)
        {
            if (value.Length < 2) return null;
            if (value.StartsWith("'''", StringComparison.Ordinal) && value.EndsWith("'''", StringComparison.Ordinal) && value.Length >= 6)
                return TrimFirstNewline(value.Substring(3, value.Length - 6));
            if (value.StartsWith("\"\"\"", StringComparison.Ordinal) && value.EndsWith("\"\"\"", StringComparison.Ordinal) && value.Length >= 6)
            {
                var content = TrimFirstNewline(value.Substring(3, value.Length - 6));
                content = Regex.Replace(content, @"\\\r?\n\s*", "");
                return JsonReader.Read("\"" + content.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"") as string;
            }
            if (value[0] == '\'' && value[value.Length - 1] == '\'') return value.Substring(1, value.Length - 2);
            if (value[0] == '"' && value[value.Length - 1] == '"') return JsonReader.Read(value) as string;
            return null;
        }

        static string TrimFirstNewline(string value) => value.StartsWith("\r\n", StringComparison.Ordinal) ? value.Substring(2)
            : value.StartsWith("\n", StringComparison.Ordinal) ? value.Substring(1) : value;

        // Validate the whole bare token before distinguishing TOML syntax errors from schema
        // failures (e.g. enabled = 1oops versus enabled = 42). Containers/strings are lexed above.
        static bool SkillValueSyntax(string value)
        {
            if ("\"'[{".IndexOf(value[0]) >= 0) return true;
            const string digits = @"[0-9](?:_?[0-9])*";
            const string integer = @"(?:0|[1-9](?:_?[0-9])*)";
            var number = @"[+\-]?" + integer + @"(?:\." + digits + @")?(?:[eE][+\-]?" + digits + @")?";
            if (Regex.IsMatch(value, @"\A(?:true|false|[+\-]?(?:inf|nan)|" + number +
                @"|0x[0-9A-Fa-f](?:_?[0-9A-Fa-f])*|0o[0-7](?:_?[0-7])*|0b[01](?:_?[01])*)\z")) return true;
            // Date/time values are valid TOML but invalid selector/boolean field types.
            return Regex.IsMatch(value, @"\A(?:[0-9]{4}-[0-9]{2}-[0-9]{2}|[0-9]{2}:[0-9]{2}:[0-9]{2})") &&
                DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }

        static int Assignment(string text)
        {
            char quote = '\0';
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quote == '"' && c == '\\') { i++; continue; }
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c == '"' || c == '\'') quote = c;
                else if (c == '=') return i;
            }
            return -1;
        }

        static List<string> Key(string text)
        {
            var parts = new List<string>();
            var position = 0;
            while (position < text.Length)
            {
                while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
                if (position == text.Length) throw new FormatException("Missing TOML key.");
                var start = position;
                if (text[position] == '"' || text[position] == '\'')
                {
                    var quote = text[position++];
                    while (position < text.Length && text[position] != quote)
                    {
                        if (quote == '"' && text[position] == '\\') position++;
                        position++;
                    }
                    if (position >= text.Length) throw new FormatException("Unclosed TOML key.");
                    var token = text.Substring(start, ++position - start);
                    parts.Add(quote == '"' ? (string)JsonReader.Read(token) : token.Substring(1, token.Length - 2));
                }
                else
                {
                    while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_' || text[position] == '-')) position++;
                    if (position == start) throw new FormatException("Invalid TOML key.");
                    parts.Add(text.Substring(start, position - start));
                }
                while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
                if (position == text.Length) break;
                if (text[position++] != '.' || position == text.Length) throw new FormatException("Invalid dotted TOML key.");
            }
            if (parts.Count == 0) throw new FormatException("Missing TOML key.");
            return parts;
        }

        // Lex just enough to avoid treating table-shaped text in a value as configuration.
        static IEnumerable<string> Statements(string text)
        {
            var line = new StringBuilder();
            var brackets = new Stack<char>();
            char quote = '\0';
            var multiline = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quote != '\0')
                {
                    line.Append(c);
                    if (quote == '"' && c == '\\')
                    {
                        if (++i == text.Length) throw new FormatException("Unclosed TOML string.");
                        line.Append(text[i]);
                    }
                    else if (c == quote)
                    {
                        if (!multiline) quote = '\0';
                        else if (i + 2 < text.Length && text[i + 1] == c && text[i + 2] == c)
                        {
                            line.Append(c).Append(c); i += 2; quote = '\0';
                        }
                    }
                    else if (!multiline && (c == '\n' || c == '\r')) throw new FormatException("Unclosed TOML string.");
                    continue;
                }
                if (c == '#')
                {
                    while (i < text.Length && text[i] != '\n') i++;
                    c = '\n';
                }
                if (c == '"' || c == '\'')
                {
                    quote = c;
                    multiline = i + 2 < text.Length && text[i + 1] == c && text[i + 2] == c;
                    if (multiline) { line.Append(c).Append(c); i += 2; }
                }
                else if (c == '[' || c == '{') brackets.Push(c);
                else if (c == ']' || c == '}')
                {
                    if (brackets.Count == 0 || brackets.Pop() != (c == ']' ? '[' : '{')) throw new FormatException("Unbalanced TOML value.");
                }
                if (c == '\n' && brackets.Count == 0)
                {
                    var statement = line.ToString().Trim();
                    if (statement.Length > 0) yield return statement;
                    line.Clear();
                }
                else line.Append(c);
            }
            if (quote != '\0' || brackets.Count > 0) throw new FormatException("Unclosed TOML value.");
            var last = line.ToString().Trim();
            if (last.Length > 0) yield return last;
        }
    }
}
