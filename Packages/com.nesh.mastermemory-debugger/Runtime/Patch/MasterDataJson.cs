using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>A JSON number kept as its original text so that 64-bit integers never lose precision.</summary>
    public readonly struct MasterDataJsonNumber : IEquatable<MasterDataJsonNumber>
    {
        public MasterDataJsonNumber(string raw)
        {
            Raw = raw ?? throw new ArgumentNullException(nameof(raw));
        }

        public string Raw { get; }

        public bool Equals(MasterDataJsonNumber other) => string.Equals(Raw, other.Raw, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is MasterDataJsonNumber other && Equals(other);
        public override int GetHashCode() => Raw?.GetHashCode() ?? 0;
        public override string ToString() => Raw;
    }

    /// <summary>JSON object that keeps insertion order.</summary>
    public sealed class MasterDataJsonObject : IEnumerable<KeyValuePair<string, object>>
    {
        readonly List<KeyValuePair<string, object>> items = new List<KeyValuePair<string, object>>();

        public int Count => items.Count;

        public object this[string key]
        {
            get => TryGetValue(key, out var value) ? value : null;
            set
            {
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i].Key == key)
                    {
                        items[i] = new KeyValuePair<string, object>(key, value);
                        return;
                    }
                }
                items.Add(new KeyValuePair<string, object>(key, value));
            }
        }

        public void Add(string key, object value) => this[key] = value;

        public bool ContainsKey(string key) => TryGetValue(key, out _);

        /// <summary>Compact JSON text, used to compare keys.</summary>
        public string ToCanonicalString() => MasterDataJson.Serialize(this, false);

        public bool TryGetValue(string key, out object value)
        {
            foreach (var item in items)
            {
                if (item.Key == key)
                {
                    value = item.Value;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Minimal JSON reader / writer.
    /// Values: null, bool, string, <see cref="MasterDataJsonNumber"/> (or any .NET numeric type when writing),
    /// <see cref="MasterDataJsonObject"/> and <see cref="List{Object}"/>.
    /// </summary>
    public static class MasterDataJson
    {
        public static string Serialize(object value, bool pretty = true)
        {
            var sb = new StringBuilder(256);
            Write(sb, value, pretty, 0);
            return sb.ToString();
        }

        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var reader = new Reader(json);
            reader.SkipWhitespace();
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.End) throw reader.Error("Unexpected trailing characters");
            return value;
        }

        // ---------------------------------------------------------------- writer

        static void Write(StringBuilder sb, object value, bool pretty, int indent)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return;
                case string s:
                    WriteString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case MasterDataJsonNumber n:
                    sb.Append(n.Raw);
                    return;
                case float f:
                    WriteFloatingPoint(sb, f.ToString("R", CultureInfo.InvariantCulture), float.IsNaN(f) || float.IsInfinity(f));
                    return;
                case double d:
                    WriteFloatingPoint(sb, d.ToString("R", CultureInfo.InvariantCulture), double.IsNaN(d) || double.IsInfinity(d));
                    return;
                case decimal m:
                    sb.Append(m.ToString(CultureInfo.InvariantCulture));
                    return;
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                    sb.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                    return;
                case MasterDataJsonObject obj:
                    WriteObject(sb, obj, pretty, indent);
                    return;
                case IList list:
                    WriteArray(sb, list, pretty, indent);
                    return;
                default:
                    WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
        }

        static void WriteFloatingPoint(StringBuilder sb, string text, bool notFinite)
        {
            if (notFinite)
            {
                // JSON has no NaN / Infinity literal
                WriteString(sb, text);
            }
            else
            {
                sb.Append(text);
            }
        }

        static void WriteObject(StringBuilder sb, MasterDataJsonObject obj, bool pretty, int indent)
        {
            if (obj.Count == 0)
            {
                sb.Append("{}");
                return;
            }
            sb.Append('{');
            var first = true;
            foreach (var pair in obj)
            {
                if (!first) sb.Append(',');
                first = false;
                NewLine(sb, pretty, indent + 1);
                WriteString(sb, pair.Key);
                sb.Append(pretty ? ": " : ":");
                Write(sb, pair.Value, pretty, indent + 1);
            }
            NewLine(sb, pretty, indent);
            sb.Append('}');
        }

        static void WriteArray(StringBuilder sb, IList list, bool pretty, int indent)
        {
            if (list.Count == 0)
            {
                sb.Append("[]");
                return;
            }
            sb.Append('[');
            for (var i = 0; i < list.Count; i++)
            {
                if (i != 0) sb.Append(',');
                NewLine(sb, pretty, indent + 1);
                Write(sb, list[i], pretty, indent + 1);
            }
            NewLine(sb, pretty, indent);
            sb.Append(']');
        }

        static void NewLine(StringBuilder sb, bool pretty, int indent)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', indent * 2);
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
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
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------------------------------------------------------------- reader

        sealed class Reader
        {
            readonly string json;
            int position;

            public Reader(string json)
            {
                this.json = json;
            }

            public bool End => position >= json.Length;

            public FormatException Error(string message)
            {
                return new FormatException($"Invalid JSON: {message} at position {position}.");
            }

            public void SkipWhitespace()
            {
                while (position < json.Length)
                {
                    var c = json[position];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') position++;
                    else break;
                }
            }

            public object ReadValue()
            {
                if (End) throw Error("Unexpected end");
                var c = json[position];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': ExpectLiteral("true"); return true;
                    case 'f': ExpectLiteral("false"); return false;
                    case 'n': ExpectLiteral("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error($"Unexpected character '{c}'");
                }
            }

            void ExpectLiteral(string literal)
            {
                if (string.CompareOrdinal(json, position, literal, 0, literal.Length) != 0) throw Error("Invalid literal");
                position += literal.Length;
            }

            MasterDataJsonObject ReadObject()
            {
                var obj = new MasterDataJsonObject();
                position++; // {
                SkipWhitespace();
                if (!End && json[position] == '}')
                {
                    position++;
                    return obj;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (End || json[position] != '"') throw Error("Expected property name");
                    var key = ReadString();
                    SkipWhitespace();
                    if (End || json[position] != ':') throw Error("Expected ':'");
                    position++;
                    SkipWhitespace();
                    obj[key] = ReadValue();
                    SkipWhitespace();
                    if (End) throw Error("Unexpected end in object");
                    var c = json[position++];
                    if (c == ',') continue;
                    if (c == '}') return obj;
                    throw Error("Expected ',' or '}'");
                }
            }

            List<object> ReadArray()
            {
                var list = new List<object>();
                position++; // [
                SkipWhitespace();
                if (!End && json[position] == ']')
                {
                    position++;
                    return list;
                }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue());
                    SkipWhitespace();
                    if (End) throw Error("Unexpected end in array");
                    var c = json[position++];
                    if (c == ',') continue;
                    if (c == ']') return list;
                    throw Error("Expected ',' or ']'");
                }
            }

            string ReadString()
            {
                position++; // "
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Error("Unterminated string");
                    var c = json[position++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (End) throw Error("Unterminated escape");
                    var e = json[position++];
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
                            if (position + 4 > json.Length) throw Error("Invalid unicode escape");
                            var hex = json.Substring(position, 4);
                            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) throw Error("Invalid unicode escape");
                            sb.Append((char)code);
                            position += 4;
                            break;
                        default:
                            throw Error($"Invalid escape '\\{e}'");
                    }
                }
            }

            MasterDataJsonNumber ReadNumber()
            {
                var start = position;
                if (json[position] == '-') position++;
                while (!End)
                {
                    var c = json[position];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') position++;
                    else break;
                }
                var raw = json.Substring(start, position - start);
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) throw Error($"Invalid number '{raw}'");
                return new MasterDataJsonNumber(raw);
            }
        }
    }
}
