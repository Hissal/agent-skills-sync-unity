using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Writes the value shapes <see cref="JsonReader"/> produces back to JSON: objects as
    /// <c>List&lt;KeyValuePair&lt;string, object&gt;&gt;</c> (indented, one member per line),
    /// arrays as <c>List&lt;object&gt;</c> (inline), strings, doubles, bools and null.
    /// </summary>
    internal static class JsonWriter
    {
        public static string Write(object value)
        {
            var text = new StringBuilder();
            WriteValue(text, value, 0);
            text.Append('\n');
            return text.ToString();
        }

        static void WriteValue(StringBuilder text, object value, int indent)
        {
            switch (value)
            {
                case null: text.Append("null"); break;
                case bool b: text.Append(b ? "true" : "false"); break;
                case string s: WriteString(text, s); break;
                case double d: text.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case int i: text.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case List<KeyValuePair<string, object>> obj: WriteObject(text, obj, indent); break;
                case List<object> array: WriteArray(text, array, indent); break;
                default: throw new ArgumentException($"Cannot write a {value.GetType().Name} as JSON.");
            }
        }

        static void WriteObject(StringBuilder text, List<KeyValuePair<string, object>> obj, int indent)
        {
            if (obj.Count == 0)
            {
                text.Append("{}");
                return;
            }

            text.Append("{\n");
            for (var i = 0; i < obj.Count; i++)
            {
                text.Append(' ', (indent + 1) * 2);
                WriteString(text, obj[i].Key);
                text.Append(": ");
                WriteValue(text, obj[i].Value, indent + 1);
                if (i < obj.Count - 1) text.Append(',');
                text.Append('\n');
            }
            text.Append(' ', indent * 2).Append('}');
        }

        static void WriteArray(StringBuilder text, List<object> array, int indent)
        {
            text.Append('[');
            for (var i = 0; i < array.Count; i++)
            {
                if (i > 0) text.Append(", ");
                WriteValue(text, array[i], indent);
            }
            text.Append(']');
        }

        static void WriteString(StringBuilder text, string s)
        {
            text.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < 0x20) text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else text.Append(c);
                        break;
                }
            }
            text.Append('"');
        }
    }
}
