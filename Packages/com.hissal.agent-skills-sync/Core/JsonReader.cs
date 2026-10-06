using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Hissal.AgentSkillsSync
{
    /// <summary>
    /// Minimal JSON reader for the core assembly, which can use neither JsonUtility nor a
    /// package dependency. Objects become ordered key/value lists, arrays become lists,
    /// numbers become doubles.
    /// </summary>
    internal sealed class JsonReader
    {
        readonly string _text;
        int _pos;

        JsonReader(string text) => _text = text;

        /// <exception cref="FormatException">The text is not valid JSON.</exception>
        public static object Read(string text)
        {
            if (text == null) throw new FormatException("No text.");
            var reader = new JsonReader(text);
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (reader._pos != text.Length) throw reader.Error("Unexpected text after the JSON value");
            return value;
        }

        object ReadValue()
        {
            SkipWhitespace();
            if (_pos >= _text.Length) throw Error("Unexpected end of input");
            var c = _text[_pos];
            switch (c)
            {
                case '{': return ReadObject();
                case '[': return ReadArray();
                case '"': return ReadString();
                case 't': ReadLiteral("true"); return true;
                case 'f': ReadLiteral("false"); return false;
                case 'n': ReadLiteral("null"); return null;
                default:
                    if (c == '-' || char.IsDigit(c)) return ReadNumber();
                    throw Error($"Unexpected character '{c}'");
            }
        }

        List<KeyValuePair<string, object>> ReadObject()
        {
            var members = new List<KeyValuePair<string, object>>();
            _pos++;
            SkipWhitespace();
            if (TryConsume('}')) return members;
            while (true)
            {
                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] != '"') throw Error("Expected a property name");
                var key = ReadString();
                SkipWhitespace();
                Expect(':');
                members.Add(new KeyValuePair<string, object>(key, ReadValue()));
                SkipWhitespace();
                if (TryConsume('}')) return members;
                Expect(',');
            }
        }

        List<object> ReadArray()
        {
            var items = new List<object>();
            _pos++;
            SkipWhitespace();
            if (TryConsume(']')) return items;
            while (true)
            {
                items.Add(ReadValue());
                SkipWhitespace();
                if (TryConsume(']')) return items;
                Expect(',');
            }
        }

        string ReadString()
        {
            _pos++;
            var sb = new StringBuilder();
            while (true)
            {
                if (_pos >= _text.Length) throw Error("Unterminated string");
                var c = _text[_pos++];
                if (c == '"') return sb.ToString();
                if (c < ' ') throw Error("Control character in string");
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (_pos >= _text.Length) throw Error("Unterminated string");
                var escape = _text[_pos++];
                switch (escape)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_pos + 4 > _text.Length
                            || !int.TryParse(_text.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            throw Error("Invalid \\u escape");
                        sb.Append((char)code);
                        _pos += 4;
                        break;
                    default: throw Error($"Invalid escape '\\{escape}'");
                }
            }
        }

        double ReadNumber()
        {
            var start = _pos;
            while (_pos < _text.Length && "+-0123456789.eE".IndexOf(_text[_pos]) >= 0) _pos++;
            var token = _text.Substring(start, _pos - start);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                throw Error($"Invalid number '{token}'");
            return number;
        }

        void ReadLiteral(string literal)
        {
            if (string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0) throw Error("Invalid literal");
            _pos += literal.Length;
        }

        void SkipWhitespace()
        {
            while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos])) _pos++;
        }

        bool TryConsume(char c)
        {
            if (_pos < _text.Length && _text[_pos] == c)
            {
                _pos++;
                return true;
            }
            return false;
        }

        void Expect(char c)
        {
            if (!TryConsume(c)) throw Error($"Expected '{c}'");
        }

        FormatException Error(string message) => new FormatException($"{message} at position {_pos}.");
    }
}
