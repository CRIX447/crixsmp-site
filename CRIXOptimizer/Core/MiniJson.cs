// CRIX OPTIMIZER - unofficial Gorilla Tag performance mod. Copyright (C) 2026 CRIX OPTIMIZER contributors.
// Licensed under the GNU General Public License v3.0 or later; see LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CRIXOptimizer.Core
{
    /// <summary>
    /// Minimal JSON reader/writer for small protocol messages (obs-websocket). Objects become
    /// Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double, plus string/bool/null.
    /// No dependency on Unity or third-party libraries, so it runs on background threads and in tests.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            var parser = new Parser(json);
            object value = parser.ReadValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
                throw new FormatException("Unexpected trailing characters in JSON at " + parser.Position);
            return value;
        }

        public static bool TryParse(string json, out object value)
        {
            try
            {
                value = Parse(json);
                return true;
            }
            catch (Exception)
            {
                value = null;
                return false;
            }
        }

        // ------------------------------------------------------------------ typed lookups

        public static Dictionary<string, object> Obj(object value, string key) =>
            value is Dictionary<string, object> d && d.TryGetValue(key, out object v) ? v as Dictionary<string, object> : null;

        public static string Str(object value, string key) =>
            value is Dictionary<string, object> d && d.TryGetValue(key, out object v) ? v as string : null;

        public static bool? Bool(object value, string key) =>
            value is Dictionary<string, object> d && d.TryGetValue(key, out object v) && v is bool b ? b : (bool?)null;

        public static double? Num(object value, string key) =>
            value is Dictionary<string, object> d && d.TryGetValue(key, out object v) && v is double n ? n : (double?)null;

        // ------------------------------------------------------------------ writing

        public static string Serialize(object value)
        {
            var sb = new StringBuilder(128);
            Write(sb, value);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case IDictionary<string, object> dict:
                    sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, object> pair in dict)
                    {
                        if (!first)
                            sb.Append(',');
                        first = false;
                        WriteString(sb, pair.Key);
                        sb.Append(':');
                        Write(sb, pair.Value);
                    }
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (object item in list)
                    {
                        if (!firstItem)
                            sb.Append(',');
                        firstItem = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    break;
                default:
                    WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ------------------------------------------------------------------ parsing

        private sealed class Parser
        {
            private const int MaxDepth = 64;
            private readonly string s;
            private int i;
            private int depth;

            public Parser(string text) { s = text; }

            public int Position => i;
            public bool AtEnd => i >= s.Length;

            public void SkipWhitespace()
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r'))
                    i++;
            }

            public object ReadValue()
            {
                SkipWhitespace();
                if (i >= s.Length)
                    throw new FormatException("Unexpected end of JSON");
                char c = s[i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return ReadNumber();
                        throw new FormatException($"Unexpected character '{c}' at {i}");
                }
            }

            private Dictionary<string, object> ReadObject()
            {
                if (++depth > MaxDepth)
                    throw new FormatException("JSON nested too deeply");
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                i++; // {
                SkipWhitespace();
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    depth--;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (i >= s.Length || s[i] != '"')
                        throw new FormatException("Expected property name at " + i);
                    string key = ReadString();
                    SkipWhitespace();
                    if (i >= s.Length || s[i] != ':')
                        throw new FormatException("Expected ':' at " + i);
                    i++;
                    result[key] = ReadValue();
                    SkipWhitespace();
                    if (i >= s.Length)
                        throw new FormatException("Unterminated object");
                    if (s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (s[i] == '}')
                    {
                        i++;
                        depth--;
                        return result;
                    }
                    throw new FormatException("Expected ',' or '}' at " + i);
                }
            }

            private List<object> ReadArray()
            {
                if (++depth > MaxDepth)
                    throw new FormatException("JSON nested too deeply");
                var result = new List<object>();
                i++; // [
                SkipWhitespace();
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    depth--;
                    return result;
                }
                while (true)
                {
                    result.Add(ReadValue());
                    SkipWhitespace();
                    if (i >= s.Length)
                        throw new FormatException("Unterminated array");
                    if (s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (s[i] == ']')
                    {
                        i++;
                        depth--;
                        return result;
                    }
                    throw new FormatException("Expected ',' or ']' at " + i);
                }
            }

            private string ReadString()
            {
                i++; // opening quote
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"')
                        return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (i >= s.Length)
                        break;
                    char e = s[i++];
                    switch (e)
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
                            if (i + 4 > s.Length)
                                throw new FormatException("Bad unicode escape");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default:
                            throw new FormatException("Bad escape \\" + e);
                    }
                }
                throw new FormatException("Unterminated string");
            }

            private double ReadNumber()
            {
                int start = i;
                if (s[i] == '-')
                    i++;
                while (i < s.Length && ((s[i] >= '0' && s[i] <= '9') || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
                    i++;
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private void Expect(string literal)
            {
                if (string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                    throw new FormatException($"Expected '{literal}' at {i}");
                i += literal.Length;
            }
        }
    }
}
