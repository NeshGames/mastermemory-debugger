using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Completion for <see cref="MasterRecordQuery"/>: field names, and enum / bool values after an operator.
    /// </summary>
    internal static class MasterRecordQueryCompletion
    {
        /// <summary>The word under the caret and what it can be completed to.</summary>
        public sealed class Context
        {
            public int Start;
            public int End;
            public string Word;
            /// <summary>Set when completing a value; null when completing a field name.</summary>
            public MasterMemoryFieldDescriptor ValueField;
            public List<string> Candidates;

            public bool IsValue => ValueField != null;
        }

        // Field op [quote] value-prefix, up to the caret. Flags values may be joined with | or ,
        static readonly Regex s_value = new Regex(@"(?:^|\s)([A-Za-z_][A-Za-z0-9_]*)\s*(?:>=|<=|!=|=|>|<)\s*""?(?:[^\s""]*[|,])?([^\s""|,]*)$", RegexOptions.CultureInvariant);
        static readonly Regex s_field = new Regex(@"(?:^|\s)([A-Za-z_][A-Za-z0-9_]*)?$", RegexOptions.CultureInvariant);
        static readonly string[] s_booleans = { "true", "false" };

        /// <summary>Returns null when nothing at the caret can be completed.</summary>
        public static Context GetContext(string text, int caret, MasterDataTypeDescriptor type)
        {
            if (type == null) return null;
            text ??= string.Empty;
            caret = Math.Max(0, Math.Min(caret, text.Length));
            var value = TryValueContext(text, caret, type);
            if (value != null || IsInsideQuotes(text, caret)) return value;

            var left = text.Substring(0, caret);
            var match = s_field.Match(left);
            if (!match.Success) return null;
            var start = match.Groups[1].Success ? match.Groups[1].Index : caret;
            var end = ExtendWord(text, caret);
            // an operator right after the word means we are on a field name; any other character, on a text term
            if (end < text.Length && !char.IsWhiteSpace(text[end]) && !IsOperatorChar(text[end])) return null;
            // "Damage > |" is a value position, not a new field name
            var before = left.Substring(0, start).TrimEnd();
            if (before.Length > 0 && IsOperatorChar(before[before.Length - 1])) return null;

            var word = left.Substring(start);
            var candidates = Filter(FieldNames(type), word);
            return candidates.Count == 0 ? null : new Context { Start = start, End = end, Word = text.Substring(start, end - start), Candidates = candidates };
        }

        /// <summary>Replaces the word of <paramref name="context"/> with <paramref name="candidate"/>.</summary>
        public static string Apply(string text, Context context, string candidate, out int caret)
        {
            text ??= string.Empty;
            caret = context.Start + candidate.Length;
            return text.Substring(0, context.Start) + candidate + text.Substring(context.End);
        }

        static Context TryValueContext(string text, int caret, MasterDataTypeDescriptor type)
        {
            var match = s_value.Match(text.Substring(0, caret));
            if (!match.Success) return null;
            if (!type.TryGetField(match.Groups[1].Value, out var field))
            {
                field = null;
                foreach (var f in type.Fields)
                {
                    if (string.Equals(f.Name, match.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) field = f;
                }
                if (field == null) return null;
            }

            IReadOnlyList<string> values;
            switch (field.Kind)
            {
                case MasterDataValueKind.Enum:
                case MasterDataValueKind.FlagsEnum:
                    values = Enum.GetNames(field.ValueType);
                    break;
                case MasterDataValueKind.Boolean:
                    values = s_booleans;
                    break;
                default:
                    return null;
            }

            var start = match.Groups[2].Index;
            var end = caret;
            while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != '"' && text[end] != '|' && text[end] != ',') end++;
            var candidates = Filter(values, match.Groups[2].Value);
            if (candidates.Count == 0) return null;
            return new Context { Start = start, End = end, Word = text.Substring(start, end - start), ValueField = field, Candidates = candidates };
        }

        static IEnumerable<string> FieldNames(MasterDataTypeDescriptor type)
        {
            foreach (var field in type.Fields) yield return field.Name;
        }

        /// <summary>Names starting with the word (declaration order); names containing it when none does.</summary>
        static List<string> Filter(IEnumerable<string> names, string word)
        {
            var prefix = new List<string>();
            var contains = new List<string>();
            foreach (var name in names)
            {
                if (name.StartsWith(word, StringComparison.OrdinalIgnoreCase)) prefix.Add(name);
                else if (word.Length > 0 && name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) contains.Add(name);
            }
            return prefix.Count > 0 ? prefix : contains;
        }

        static int ExtendWord(string text, int index)
        {
            while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_')) index++;
            return index;
        }

        static bool IsOperatorChar(char c) => "=!<>~".IndexOf(c) >= 0;

        static bool IsInsideQuotes(string text, int caret)
        {
            var quotes = 0;
            for (var i = 0; i < caret; i++)
            {
                if (text[i] == '"') quotes++;
            }
            return quotes % 2 == 1;
        }
    }
}
