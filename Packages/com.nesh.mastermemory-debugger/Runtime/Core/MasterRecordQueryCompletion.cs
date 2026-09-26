using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Tab completion for <see cref="MasterRecordQuery"/>: field names, and enum / bool values after an operator.
    /// Tab completes the common prefix first, the next Tabs cycle through the candidates (Shift+Tab backwards).
    /// </summary>
    internal sealed class MasterRecordQueryCompletion
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

        string cycleText;
        int cycleCaret;
        int cycleStart;
        int cycleIndex;
        List<string> cycleCandidates;

        /// <summary>The candidate shown by the last cycling Tab, or null.</summary>
        public string CurrentCandidate => cycleCandidates != null ? cycleCandidates[cycleIndex] : null;

        public void Reset()
        {
            cycleCandidates = null;
            cycleText = null;
        }

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

        /// <summary>
        /// Completes the word at the caret. Returns false when there is nothing to complete.
        /// </summary>
        public bool TryComplete(string text, int caret, MasterDataTypeDescriptor type, bool backwards, out string newText, out int newCaret)
        {
            text ??= string.Empty;
            newText = text;
            newCaret = caret;

            if (IsCycling(text, caret))
            {
                var count = cycleCandidates.Count;
                var current = cycleCandidates[cycleIndex];
                cycleIndex = (cycleIndex + (backwards ? count - 1 : 1)) % count;
                Replace(text, cycleStart, cycleStart + current.Length, cycleCandidates[cycleIndex], out newText, out newCaret);
                cycleText = newText;
                cycleCaret = newCaret;
                return true;
            }

            Reset();
            var context = GetContext(text, caret, type);
            if (context == null) return false;

            var candidates = context.Candidates;
            if (candidates.Count == 1)
            {
                return Replace(text, context.Start, context.End, candidates[0], out newText, out newCaret);
            }

            var common = CommonPrefix(candidates);
            if (common.Length > context.Word.Length && common.StartsWith(context.Word, StringComparison.OrdinalIgnoreCase))
            {
                return Replace(text, context.Start, context.End, common, out newText, out newCaret);
            }

            var index = candidates.FindIndex(x => string.Equals(x, context.Word, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = backwards ? candidates.Count - 1 : 0;
            else index = (index + (backwards ? candidates.Count - 1 : 1)) % candidates.Count;

            Replace(text, context.Start, context.End, candidates[index], out newText, out newCaret);
            cycleCandidates = candidates;
            cycleIndex = index;
            cycleStart = context.Start;
            cycleText = newText;
            cycleCaret = newCaret;
            return true;
        }

        /// <summary>True while the last Tab cycled and the text has not been edited since.</summary>
        public bool IsCycling(string text, int caret) => cycleCandidates != null && text == cycleText && caret == cycleCaret;

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

        static string CommonPrefix(List<string> names)
        {
            var common = names[0];
            for (var i = 1; i < names.Count && common.Length > 0; i++)
            {
                var length = 0;
                var max = Math.Min(common.Length, names[i].Length);
                while (length < max && char.ToLowerInvariant(common[length]) == char.ToLowerInvariant(names[i][length])) length++;
                common = common.Substring(0, length);
            }
            return common;
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

        static bool Replace(string text, int start, int end, string replacement, out string newText, out int newCaret)
        {
            newText = text.Substring(0, start) + replacement + text.Substring(end);
            newCaret = start + replacement.Length;
            return newText != text;
        }
    }
}
