using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Search box query. Adjacent space-separated terms are ANDed; explicit && and || support grouping with parentheses.
    /// <list type="bullet">
    /// <item><c>Field op Value</c> with op <c>= != &gt; &gt;= &lt; &lt;= ~</c> (<c>~</c> = contains), e.g. <c>Damage&gt;100 Element=Fire</c></item>
    /// <item>any other text: matches the primary key, the display name or a string member (contains, case-insensitive)</item>
    /// </list>
    /// && binds tighter than ||. Values with spaces are quoted: <c>Name="Ice Blast"</c>. <c>Field=null</c> matches null values.
    /// Values of members with a converter are read with it; <c>&gt; &gt;= &lt; &lt;=</c> need a converter that is an
    /// <see cref="IComparer"/>. Conditions read the current value (override when present).
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
            public object CustomValue;
        }

        abstract class QueryNode
        {
            public abstract bool Matches(MasterMemoryRecordDescriptor record);
        }

        sealed class TermNode : QueryNode
        {
            public Condition Condition;
            public string Text;

            public override bool Matches(MasterMemoryRecordDescriptor record)
            {
                if (Condition == null) return record.Matches(Text);
                var current = record.Current;
                return current != null && Evaluate(Condition, Condition.Field.GetValue(current));
            }
        }

        sealed class BinaryNode : QueryNode
        {
            public QueryNode Left;
            public QueryNode Right;
            public bool IsOr;

            public override bool Matches(MasterMemoryRecordDescriptor record) =>
                IsOr ? Left.Matches(record) || Right.Matches(record) : Left.Matches(record) && Right.Matches(record);
        }

        static readonly Regex s_condition = new Regex(@"^([A-Za-z_][A-Za-z0-9_]*)(>=|<=|!=|=|>|<|~)(.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

        readonly List<Condition> conditions = new List<Condition>();
        readonly List<string> textTerms = new List<string>();
        readonly List<string> errors = new List<string>();
        QueryNode expression;
        bool invalidExpression;
        bool hasBooleanSyntax;

        MasterRecordQuery()
        {
        }

        public static MasterRecordQuery Empty { get; } = new MasterRecordQuery();

        public bool IsEmpty => !invalidExpression && conditions.Count == 0 && textTerms.Count == 0;

        public bool HasBooleanSyntax => hasBooleanSyntax;

        /// <summary>Problems found while parsing (unknown fields, invalid values). Those terms are ignored.</summary>
        public IReadOnlyList<string> Errors => errors;

        public int ConditionCount => conditions.Count;

        public IReadOnlyList<string> TextTerms => textTerms;

        public static MasterRecordQuery Parse(string text, MasterDataTypeDescriptor type)
        {
            var query = new MasterRecordQuery();
            if (string.IsNullOrWhiteSpace(text)) return query;

            var tokens = new List<string>(Tokenize(NormalizeOperators(text.Trim())));
            query.hasBooleanSyntax = tokens.Exists(IsBooleanToken);
            if (!query.hasBooleanSyntax)
            {
                foreach (var token in tokens) ParseTerm(query, token, type);
                return query;
            }

            var position = 0;
            query.expression = ParseOr(query, tokens, type, ref position);
            if (position < tokens.Count) query.errors.Add($"Unexpected '{tokens[position]}'");
            // A malformed grouped query must never widen a filter, especially before Batch Edit.
            query.invalidExpression = query.expression == null || query.errors.Count > 0;
            return query;
        }

        public bool Matches(MasterMemoryRecordDescriptor record)
        {
            if (invalidExpression) return false;
            if (expression != null) return expression.Matches(record);
            if (IsEmpty) return true;
            foreach (var term in textTerms)
            {
                if (!record.Matches(term)) return false;
            }
            if (conditions.Count == 0) return true;

            var current = record.Current;
            if (current == null) return false;
            foreach (var condition in conditions)
            {
                if (!Evaluate(condition, condition.Field.GetValue(current))) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ parse

        static bool IsBooleanToken(string token) => token == "&&" || token == "||" || token == "(" || token == ")";

        static QueryNode ParseOr(MasterRecordQuery query, List<string> tokens, MasterDataTypeDescriptor type, ref int position)
        {
            var left = ParseAnd(query, tokens, type, ref position);
            while (position < tokens.Count && tokens[position] == "||")
            {
                position++;
                var right = ParseAnd(query, tokens, type, ref position);
                left = left == null || right == null ? null : new BinaryNode { Left = left, Right = right, IsOr = true };
            }
            return left;
        }

        static QueryNode ParseAnd(MasterRecordQuery query, List<string> tokens, MasterDataTypeDescriptor type, ref int position)
        {
            var left = ParsePrimary(query, tokens, type, ref position);
            while (position < tokens.Count && tokens[position] != ")" && tokens[position] != "||")
            {
                if (tokens[position] == "&&") position++;
                var right = ParsePrimary(query, tokens, type, ref position);
                left = left == null || right == null ? null : new BinaryNode { Left = left, Right = right };
            }
            return left;
        }

        static QueryNode ParsePrimary(MasterRecordQuery query, List<string> tokens, MasterDataTypeDescriptor type, ref int position)
        {
            if (position >= tokens.Count)
            {
                query.errors.Add("Expected a search term");
                return null;
            }
            var token = tokens[position++];
            if (token == "(")
            {
                if (position < tokens.Count && tokens[position] == ")")
                {
                    position++;
                    query.errors.Add("Empty group");
                    return null;
                }
                var inner = ParseOr(query, tokens, type, ref position);
                if (position >= tokens.Count || tokens[position] != ")") query.errors.Add("Missing ')'");
                else position++;
                return inner;
            }
            if (IsBooleanToken(token))
            {
                query.errors.Add($"Expected a search term before '{token}'");
                return null;
            }
            return ParseTerm(query, token, type);
        }

        static QueryNode ParseTerm(MasterRecordQuery query, string token, MasterDataTypeDescriptor type)
        {
            var match = s_condition.Match(token);
            if (!match.Success || type == null)
            {
                var text = Unquote(token);
                query.textTerms.Add(text);
                return new TermNode { Text = text };
            }

            var field = FindField(type, match.Groups[1].Value);
            if (field == null)
            {
                query.errors.Add($"Unknown field '{match.Groups[1].Value}'");
                return null;
            }

            var condition = CreateCondition(field, ParseOperator(match.Groups[2].Value), Unquote(match.Groups[3].Value), out var error);
            if (condition == null)
            {
                query.errors.Add(error);
                return null;
            }
            query.conditions.Add(condition);
            return new TermNode { Condition = condition };
        }

        // Remove spaces around field comparison operators without changing quoted values.
        static string NormalizeOperators(string text)
        {
            var result = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '"')
                {
                    quoted = !quoted;
                    result.Append(c);
                    continue;
                }
                if (!quoted && (c == '=' || c == '>' || c == '<' || c == '~' || (c == '!' && i + 1 < text.Length && text[i + 1] == '=')))
                {
                    while (result.Length > 0 && char.IsWhiteSpace(result[result.Length - 1])) result.Length--;
                    result.Append(c);
                    if (i + 1 < text.Length && text[i + 1] == '=' && c != '=') result.Append(text[++i]);
                    while (i + 1 < text.Length && char.IsWhiteSpace(text[i + 1])) i++;
                    continue;
                }
                result.Append(c);
            }
            return result.ToString();
        }

        static IEnumerable<string> Tokenize(string text)
        {
            var word = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '"')
                {
                    quoted = !quoted;
                    word.Append(c);
                    continue;
                }
                if (!quoted && (c == '(' || c == ')' || (c == '&' && i + 1 < text.Length && text[i + 1] == '&') || (c == '|' && i + 1 < text.Length && text[i + 1] == '|')))
                {
                    if (word.Length > 0) { yield return word.ToString(); word.Clear(); }
                    if (c == '&' || c == '|') { yield return new string(c, 2); i++; }
                    else yield return c.ToString();
                    continue;
                }
                if (!quoted && char.IsWhiteSpace(c))
                {
                    if (word.Length > 0) { yield return word.ToString(); word.Clear(); }
                    continue;
                }
                word.Append(c);
            }
            if (word.Length > 0) yield return word.ToString();
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
                        condition.EnumValue = MasterDataValueUtility.ParseEnum(field.ValueType, field.Kind == MasterDataValueKind.FlagsEnum ? value.Replace('|', ',') : value);
                        return condition;
                    }
                    catch (Exception)
                    {
                        error = $"{field.Name}: '{value}' is not one of {string.Join(", ", Enum.GetNames(field.ValueType))}";
                        return null;
                    }
                case MasterDataValueKind.Custom:
                    if (IsOrdering(op) && !(field.Converter is IComparer))
                    {
                        error = $"{field.Name}: values of {field.ValueType.Name} can only be compared with = or !=";
                        return null;
                    }
                    if (!field.Converter.TryParse(value, out condition.CustomValue, out var parseError))
                    {
                        error = $"{field.Name}: {parseError}";
                        return null;
                    }
                    return condition;
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
                case MasterDataValueKind.Custom:
                    comparison = condition.Field.Converter is IComparer comparer
                        ? comparer.Compare(value, condition.CustomValue)
                        : MasterDataValueUtility.AreEqual(value, condition.CustomValue) ? 0 : 1;
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
