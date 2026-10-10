using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Reads only plugins."id" tables and their enabled boolean. Other TOML values are skipped as complete
    /// statements, including multiline arrays, inline tables and strings. Invalid relevant state fails closed.
    /// This is a config subset reader, not a general TOML parser.
    /// </summary>
    internal static class CodexPluginConfig
    {
        public static Dictionary<string, bool?> Read(string path)
        {
            var result = new Dictionary<string, bool?>(StringComparer.Ordinal);
            string plugin = null;
            var tables = new HashSet<string>(StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            string table = "";
            string text;
            try { text = File.ReadAllText(path); }
            catch (FileNotFoundException) { return result; }
            catch (DirectoryNotFoundException) { return result; }
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
                if (plugin == null || key.Count != 1 || key[0] != "enabled") continue;
                if (value != "true" && value != "false") throw new FormatException("Plugin enabled must be a boolean.");
                result[plugin] = value == "true";
            }
            return result;
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
