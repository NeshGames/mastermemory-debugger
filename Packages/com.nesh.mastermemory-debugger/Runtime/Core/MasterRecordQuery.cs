using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Search box query. Space separated terms that must all match:
    /// <list type="bullet">
    /// <item><c>Field op Value</c> with op <c>= != &gt; &gt;= &lt; &lt;= ~</c> (<c>~</c> = contains), e.g. <c>Damage&gt;100 Element=Fire</c></item>
    /// <item>any other text: matches the primary key, the display name or a string member (contains, case-insensitive)</item>
    /// </list>
    /// Values with spaces are quoted: <c>Name="Ice Blast"</c>. <c>Field=null</c> matches null values.
    /// Conditions read the current value (override when present).
    /// </summary>
    public sealed class MasterRecordQuery
    {
        enum Operator
        {
            Equal,
            NotEqual,
            Greater,
            GreaterOrEqual,
            Less,
            LessOrEqual,
            Contains,
        }

        sealed class Condition
        {
            public MasterMemoryFieldDescriptor Field;
            public Operator Operator;
            public string Text;
            public bool IsNull;
            public decimal Integer;
            public double Real;
            public bool Boolean;
            public object EnumValue;
        }

        static readonly Regex s_operatorSpacing = new Regex(@"\s*(>=|<=|!=|=|>|<|~)\s*", RegexOptions.CultureInvariant);
        static readonly Regex s_condition = new Regex(@"^([A-Za-z_][A-Za-z0-9_]*)(>=|<=|!=|=|>|<|~)(.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

        readonly List<Condition> conditions = new List<Condition>();
        readonly List<string> textTerms = new List<string>();
        readonly List<string> errors = new List<string>();

        MasterRecordQuery()
        {
        }

        public static MasterRecordQuery Empty { get; } = new MasterRecordQuery();

        public bool IsEmpty => conditions.Count == 0 && textTerms.Count == 0;

        /// <summary>Problems found while parsing (unknown fields, invalid values). Those terms are ignored.</summary>
        public IReadOnlyList<string> Errors => errors;

        public int ConditionCount => conditions.Count;

        public IReadOnlyList<string> TextTerms => textTerms;

        public static MasterRecordQuery Parse(string text, MasterDataTypeDescriptor type)
        {
            var query = new MasterRecordQuery();
            if (string.IsNullOrWhiteSpace(text)) return query;

            foreach (var token in Tokenize(s_operatorSpacing.Replace(text.Trim(), "$1")))
            {
                var match = s_condition.Match(token);
                if (!match.Success || type == null)
                {
                    query.textTerms.Add(Unquote(token));
                    continue;
                }

                var field = FindField(type, match.Groups[1].Value);
                if (field == null)
                {
                    query.errors.Add($"Unknown field '{match.Groups[1].Value}'");
                    continue;
                }

                var condition = CreateCondition(field, ParseOperator(match.Groups[2].Value), Unquote(match.Groups[3].Value), out var error);
                if (condition == null) query.errors.Add(error);
                else query.conditions.Add(condition);
            }
            return query;
        }

        public bool Matches(MasterMemoryRecordDescriptor record)
        {
            if (IsEmpty) return true;
            foreach (var term in textTerms)
            {
                if (!record.Matches(term)) return false;
            }
            if (conditions.Count == 0) return true;

            var current = record.Current;
            foreach (var condition in conditions)
            {
                if (!Evaluate(condition, condition.Field.GetValue(current))) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ parse

        static IEnumerable<string> Tokenize(string text)
        {
            var sb = new StringBuilder();
            var quoted = false;
            foreach (var c in text)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    sb.Append(c);
                }
                else if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (sb.Length > 0) yield return sb.ToString();
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        static string Unquote(string text) => text.Replace("\"", string.Empty);

        static MasterMemoryFieldDescriptor FindField(MasterDataTypeDescriptor type, string name)
        {
            if (type.TryGetField(name, out var exact)) return exact;
            foreach (var field in type.Fields)
            {
                if (string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase)) return field;
            }
            return null;
        }

        static Operator ParseOperator(string text)
        {
            switch (text)
            {
                case "=": return Operator.Equal;
                case "!=": return Operator.NotEqual;
                case ">": return Operator.Greater;
                case ">=": return Operator.GreaterOrEqual;
                case "<": return Operator.Less;
                case "<=": return Operator.LessOrEqual;
                default: return Operator.Contains;
            }
        }

        static bool IsOrdering(Operator op) => op == Operator.Greater || op == Operator.GreaterOrEqual || op == Operator.Less || op == Operator.LessOrEqual;

        static Condition CreateCondition(MasterMemoryFieldDescriptor field, Operator op, string value, out string error)
        {
            error = null;
            var condition = new Condition { Field = field, Operator = op, Text = value };
            if (op == Operator.Contains) return condition;

            if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase) && (field.IsNullable || !field.ValueType.IsValueType))
            {
                if (IsOrdering(op))
                {
                    error = $"{field.Name}: null can only be compared with = or !=";
                    return null;
                }
                condition.IsNull = true;
                return condition;
            }

            switch (field.Kind)
            {
                case MasterDataValueKind.Int32:
                case MasterDataValueKind.UInt32:
                case MasterDataValueKind.Int16:
                case MasterDataValueKind.UInt16:
                case MasterDataValueKind.Int64:
                case MasterDataValueKind.UInt64:
                case MasterDataValueKind.Byte:
                case MasterDataValueKind.SByte:
                    if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out condition.Integer))
                    {
                        error = $"{field.Name}: '{value}' is not a number";
                        return null;
                    }
                    return condition;
                case MasterDataValueKind.Single:
                case MasterDataValueKind.Double:
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out condition.Real))
                    {
                        error = $"{field.Name}: '{value}' is not a number";
                        return null;
                    }
                    return condition;
                case MasterDataValueKind.Boolean:
                    if (IsOrdering(op) || !TryParseBool(value, out condition.Boolean))
                    {
                        error = $"{field.Name}: use {field.Name}=true or {field.Name}=false";
                        return null;
                    }
                    return condition;
                case MasterDataValueKind.Enum:
                case MasterDataValueKind.FlagsEnum:
                    try
                    {
                        condition.EnumValue = MasterDataValueUtility.ParseEnum(field.ValueType, value);
                        return condition;
                    }
                    catch (Exception)
                    {
                        error = $"{field.Name}: '{value}' is not one of {string.Join(", ", Enum.GetNames(field.ValueType))}";
                        return null;
                    }
                default:
                    // strings, vectors, colors, complex: text comparison
                    return condition;
            }
        }

        static bool TryParseBool(string value, out bool result)
        {
            switch (value.ToLowerInvariant())
            {
                case "true":
                case "1":
                    result = true;
                    return true;
                case "false":
                case "0":
                    result = false;
                    return true;
                default:
                    result = false;
                    return false;
            }
        }

        // ------------------------------------------------------------------ evaluate

        static bool Evaluate(Condition condition, object value)
        {
            var op = condition.Operator;
            if (op == Operator.Contains)
            {
                return value != null && MasterDataValueUtility.Format(value).IndexOf(condition.Text, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (condition.IsNull)
            {
                return op == Operator.Equal ? value == null : value != null;
            }
            if (value == null)
            {
                return op == Operator.NotEqual;
            }

            int comparison;
            switch (condition.Field.Kind)
            {
                case MasterDataValueKind.Int32:
                case MasterDataValueKind.UInt32:
                case MasterDataValueKind.Int16:
                case MasterDataValueKind.UInt16:
                case MasterDataValueKind.Int64:
                case MasterDataValueKind.UInt64:
                case MasterDataValueKind.Byte:
                case MasterDataValueKind.SByte:
                    comparison = Convert.ToDecimal(value, CultureInfo.InvariantCulture).CompareTo(condition.Integer);
                    break;
                case MasterDataValueKind.Single:
                case MasterDataValueKind.Double:
                    comparison = Convert.ToDouble(value, CultureInfo.InvariantCulture).CompareTo(condition.Real);
                    break;
                case MasterDataValueKind.Boolean:
                    comparison = (bool)value == condition.Boolean ? 0 : 1;
                    break;
                case MasterDataValueKind.Enum:
                case MasterDataValueKind.FlagsEnum:
                    comparison = Convert.ToInt64(value, CultureInfo.InvariantCulture).CompareTo(Convert.ToInt64(condition.EnumValue, CultureInfo.InvariantCulture));
                    break;
                default:
                    comparison = string.Compare(MasterDataValueUtility.Format(value), condition.Text, StringComparison.OrdinalIgnoreCase);
                    break;
            }

            switch (op)
            {
                case Operator.Equal: return comparison == 0;
                case Operator.NotEqual: return comparison != 0;
                case Operator.Greater: return comparison > 0;
                case Operator.GreaterOrEqual: return comparison >= 0;
                case Operator.Less: return comparison < 0;
                default: return comparison <= 0;
            }
        }
    }
}
