using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Conversion of member values to display text and to / from JSON.</summary>
    public static class MasterDataValueUtility
    {
        const int MaxPreviewItems = 8;

        public static MasterDataValueKind GetKind(Type type)
        {
            if (type == typeof(string)) return MasterDataValueKind.String;
            if (type == typeof(bool)) return MasterDataValueKind.Boolean;
            if (type == typeof(int)) return MasterDataValueKind.Int32;
            if (type == typeof(uint)) return MasterDataValueKind.UInt32;
            if (type == typeof(short)) return MasterDataValueKind.Int16;
            if (type == typeof(ushort)) return MasterDataValueKind.UInt16;
            if (type == typeof(long)) return MasterDataValueKind.Int64;
            if (type == typeof(ulong)) return MasterDataValueKind.UInt64;
            if (type == typeof(byte)) return MasterDataValueKind.Byte;
            if (type == typeof(sbyte)) return MasterDataValueKind.SByte;
            if (type == typeof(float)) return MasterDataValueKind.Single;
            if (type == typeof(double)) return MasterDataValueKind.Double;
            if (type == typeof(Vector2)) return MasterDataValueKind.Vector2;
            if (type == typeof(Vector3)) return MasterDataValueKind.Vector3;
            if (type == typeof(Vector2Int)) return MasterDataValueKind.Vector2Int;
            if (type == typeof(Vector3Int)) return MasterDataValueKind.Vector3Int;
            if (type == typeof(Color)) return MasterDataValueKind.Color;
            if (type.IsEnum)
            {
                return type.IsDefined(typeof(FlagsAttribute), false) ? MasterDataValueKind.FlagsEnum : MasterDataValueKind.Enum;
            }
            return MasterDataValueKind.Complex;
        }

        // ------------------------------------------------------------------ lists

        /// <summary>
        /// True for one-dimensional arrays, <see cref="List{T}"/> and the list interfaces an array implements
        /// (<c>IReadOnlyList&lt;T&gt;</c>, <c>IList&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>, ...) whose element type is a simple, non-nullable value.
        /// </summary>
        public static bool TryGetEditableListElement(Type type, out Type elementType)
        {
            elementType = null;
            if (type == null || type == typeof(string)) return false;
            Type candidate = null;
            if (type.IsArray)
            {
                if (type.GetArrayRank() == 1) candidate = type.GetElementType();
            }
            else if (type.IsGenericType && type.GetGenericArguments().Length == 1)
            {
                var argument = type.GetGenericArguments()[0];
                if (type.GetGenericTypeDefinition() == typeof(List<>) || (type.IsInterface && type.IsAssignableFrom(argument.MakeArrayType())))
                {
                    candidate = argument;
                }
            }
            if (candidate == null || Nullable.GetUnderlyingType(candidate) != null || GetKind(candidate) == MasterDataValueKind.Complex) return false;
            elementType = candidate;
            return true;
        }

        /// <summary>A new collection of <paramref name="listType"/> (List&lt;T&gt; for List, an array otherwise).</summary>
        public static object CreateList(Type listType, Type elementType, IList items)
        {
            if (listType.IsGenericType && listType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(listType, items.Count);
                foreach (var item in items) list.Add(item);
                return list;
            }
            var array = Array.CreateInstance(elementType, items.Count);
            for (var i = 0; i < items.Count; i++) array.SetValue(items[i], i);
            return array;
        }

        /// <summary>Default value of a new list element ("" for strings).</summary>
        public static object CreateDefaultElement(Type elementType)
        {
            if (elementType == typeof(string)) return string.Empty;
            if (elementType.IsEnum)
            {
                var values = Enum.GetValues(elementType);
                return values.Length > 0 ? values.GetValue(0) : Activator.CreateInstance(elementType);
            }
            return Activator.CreateInstance(elementType);
        }

        // ------------------------------------------------------------------ display

        /// <summary>Human readable text of any value. Collections are previewed.</summary>
        public static string Format(object value)
        {
            switch (value)
            {
                case null: return "null";
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case Vector2 v: return $"({Format(v.x)}, {Format(v.y)})";
                case Vector3 v: return $"({Format(v.x)}, {Format(v.y)}, {Format(v.z)})";
                case Vector2Int v: return $"({v.x}, {v.y})";
                case Vector3Int v: return $"({v.x}, {v.y}, {v.z})";
                case Color c: return $"RGBA({Format(c.r)}, {Format(c.g)}, {Format(c.b)}, {Format(c.a)})";
                case ITuple tuple: return FormatTuple(tuple);
                case IDictionary dictionary: return FormatDictionary(dictionary);
                case IEnumerable enumerable: return FormatEnumerable(enumerable);
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }

        /// <summary>Text of a primary key. Composite keys are shown as "(a, b)".</summary>
        public static string FormatKey(object key) => Format(key);

        static string FormatTuple(ITuple tuple)
        {
            var sb = new StringBuilder("(");
            for (var i = 0; i < tuple.Length; i++)
            {
                if (i != 0) sb.Append(", ");
                sb.Append(Format(tuple[i]));
            }
            return sb.Append(')').ToString();
        }

        static string FormatEnumerable(IEnumerable enumerable)
        {
            var sb = new StringBuilder();
            var count = 0;
            foreach (var item in enumerable)
            {
                if (count < MaxPreviewItems)
                {
                    if (count != 0) sb.Append(", ");
                    sb.Append(Format(item));
                }
                count++;
            }
            if (count > MaxPreviewItems) sb.Append(", ...");
            return $"[{count}] {sb}";
        }

        static string FormatDictionary(IDictionary dictionary)
        {
            var sb = new StringBuilder();
            var count = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (count < MaxPreviewItems)
                {
                    if (count != 0) sb.Append(", ");
                    sb.Append(Format(entry.Key)).Append(": ").Append(Format(entry.Value));
                }
                count++;
            }
            if (count > MaxPreviewItems) sb.Append(", ...");
            return $"{{{count}}} {sb}";
        }

        // ------------------------------------------------------------------ equality

        /// <summary>Value equality. Collections (other than strings) are compared element by element.</summary>
        public static bool AreEqual(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Equals(b)) return true;
            if (a is string || b is string) return false;
            if (a is IEnumerable left && b is IEnumerable right) return SequenceEqual(left, right);
            return false;
        }

        static bool SequenceEqual(IEnumerable left, IEnumerable right)
        {
            var l = left.GetEnumerator();
            var r = right.GetEnumerator();
            while (true)
            {
                var hasLeft = l.MoveNext();
                var hasRight = r.MoveNext();
                if (hasLeft != hasRight) return false;
                if (!hasLeft) return true;
                if (!AreEqual(l.Current, r.Current)) return false;
            }
        }

        // ------------------------------------------------------------------ json

        /// <summary>Converts a simple value, or a list of simple values, to a JSON value. Other complex values are not supported.</summary>
        public static object ToJson(object value)
        {
            switch (value)
            {
                case null: return null;
                case string _:
                case bool _:
                    return value;
                case IList list when TryGetEditableListElement(value.GetType(), out _):
                {
                    var json = new List<object>(list.Count);
                    foreach (var item in list) json.Add(ToJson(item));
                    return json;
                }
                case Enum e: return e.ToString();
                case Vector2 v:
                    return new MasterDataJsonObject { { "x", v.x }, { "y", v.y } };
                case Vector3 v:
                    return new MasterDataJsonObject { { "x", v.x }, { "y", v.y }, { "z", v.z } };
                case Vector2Int v:
                    return new MasterDataJsonObject { { "x", v.x }, { "y", v.y } };
                case Vector3Int v:
                    return new MasterDataJsonObject { { "x", v.x }, { "y", v.y }, { "z", v.z } };
                case Color c:
                    return new MasterDataJsonObject { { "r", c.r }, { "g", c.g }, { "b", c.b }, { "a", c.a } };
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case float _:
                case double _:
                    return value;
                default:
                    throw new NotSupportedException($"Type {value.GetType().FullName} can not be converted to a patch value.");
            }
        }

        /// <summary>Converts a JSON value to a value of the member type (supports <see cref="Nullable{T}"/>).</summary>
        public static object FromJson(object json, Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            if (json == null)
            {
                if (underlying != null || !type.IsValueType) return null;
                throw new FormatException($"null is not a valid {type.Name} value.");
            }
            type = underlying ?? type;

            if (TryGetEditableListElement(type, out var elementType))
            {
                if (!(json is List<object> array)) throw new FormatException($"{type.Name} expects a JSON array.");
                var items = new List<object>(array.Count);
                foreach (var item in array) items.Add(FromJson(item, elementType));
                return CreateList(type, elementType, items);
            }

            switch (GetKind(type))
            {
                case MasterDataValueKind.String:
                    return json as string ?? ToText(json);
                case MasterDataValueKind.Boolean:
                    if (json is bool b) return b;
                    return bool.Parse(ToText(json));
                case MasterDataValueKind.Int32: return (int)ParseInteger(json, type);
                case MasterDataValueKind.UInt32: return (uint)ParseInteger(json, type);
                case MasterDataValueKind.Int16: return (short)ParseInteger(json, type);
                case MasterDataValueKind.UInt16: return (ushort)ParseInteger(json, type);
                case MasterDataValueKind.Int64: return (long)ParseInteger(json, type);
                case MasterDataValueKind.UInt64: return (ulong)ParseInteger(json, type);
                case MasterDataValueKind.Byte: return (byte)ParseInteger(json, type);
                case MasterDataValueKind.SByte: return (sbyte)ParseInteger(json, type);
                case MasterDataValueKind.Single: return (float)ParseDouble(json);
                case MasterDataValueKind.Double: return ParseDouble(json);
                case MasterDataValueKind.Enum:
                case MasterDataValueKind.FlagsEnum:
                    if (json is MasterDataJsonNumber number)
                    {
                        return Enum.ToObject(type, long.Parse(number.Raw, NumberStyles.Integer, CultureInfo.InvariantCulture));
                    }
                    return ParseEnum(type, ToText(json));
                case MasterDataValueKind.Vector2:
                {
                    var o = AsObject(json);
                    return new Vector2((float)ParseDouble(o["x"]), (float)ParseDouble(o["y"]));
                }
                case MasterDataValueKind.Vector3:
                {
                    var o = AsObject(json);
                    return new Vector3((float)ParseDouble(o["x"]), (float)ParseDouble(o["y"]), (float)ParseDouble(o["z"]));
                }
                case MasterDataValueKind.Vector2Int:
                {
                    var o = AsObject(json);
                    return new Vector2Int((int)ParseInteger(o["x"], typeof(int)), (int)ParseInteger(o["y"], typeof(int)));
                }
                case MasterDataValueKind.Vector3Int:
                {
                    var o = AsObject(json);
                    return new Vector3Int((int)ParseInteger(o["x"], typeof(int)), (int)ParseInteger(o["y"], typeof(int)), (int)ParseInteger(o["z"], typeof(int)));
                }
                case MasterDataValueKind.Color:
                {
                    var o = AsObject(json);
                    var a = o.ContainsKey("a") ? (float)ParseDouble(o["a"]) : 1f;
                    return new Color((float)ParseDouble(o["r"]), (float)ParseDouble(o["g"]), (float)ParseDouble(o["b"]), a);
                }
                default:
                    throw new NotSupportedException($"Type {type.FullName} can not be read from a patch value.");
            }
        }

        public static object ParseEnum(Type enumType, string text)
        {
            return Enum.Parse(enumType, text, true);
        }

        static MasterDataJsonObject AsObject(object json)
        {
            return json as MasterDataJsonObject ?? throw new FormatException("JSON object expected.");
        }

        static string ToText(object json)
        {
            switch (json)
            {
                case null: return null;
                case string s: return s;
                case MasterDataJsonNumber n: return n.Raw;
                case bool b: return b ? "true" : "false";
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                default: throw new FormatException("Scalar JSON value expected.");
            }
        }

        static decimal ParseInteger(object json, Type type)
        {
            if (json == null) throw new FormatException($"Missing {type.Name} value.");
            var text = ToText(json);
            var d = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (d != decimal.Truncate(d)) throw new FormatException($"'{text}' is not an integer.");

            // range check (throws OverflowException)
            Convert.ChangeType(d, type, CultureInfo.InvariantCulture);
            return d;
        }

        static double ParseDouble(object json)
        {
            if (json == null) throw new FormatException("Missing number.");
            var text = ToText(json);
            switch (text)
            {
                case "NaN": return double.NaN;
                case "Infinity": return double.PositiveInfinity;
                case "-Infinity": return double.NegativeInfinity;
            }
            return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
