using System;
using System.Collections;
using System.Collections.Generic;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>One value of a record that matches a <see cref="MasterMemoryGlobalSearch"/>.</summary>
    public sealed class MasterMemorySearchHit
    {
        internal MasterMemorySearchHit(MasterMemoryRecordDescriptor record, MasterMemoryFieldDescriptor field, string path, object value)
        {
            Record = record;
            Field = field;
            Path = path;
            Value = value;
        }

        public MasterMemoryRecordDescriptor Record { get; }

        public MasterMemoryTableDescriptor Table => Record.Table;

        /// <summary>The member of the record that holds the value.</summary>
        public MasterMemoryFieldDescriptor Field { get; }

        /// <summary>
        /// Where the value is inside <see cref="Field"/>: "" for the member itself, "[2]" for a list element,
        /// ".Attack" for a member of a nested object, "[key]" for a dictionary entry.
        /// </summary>
        public string Path { get; }

        /// <summary>The matching value (the current one: overrides included).</summary>
        public object Value { get; }

        public override string ToString() => $"{Table.TableName} {Record.KeyText} {Field.Name}{Path}: {MasterDataValueUtility.Format(Value)}";
    }

    public sealed class MasterMemorySearchResult
    {
        /// <summary>The first matches, in table registration and record order.</summary>
        public readonly List<MasterMemorySearchHit> Hits = new List<MasterMemorySearchHit>();

        /// <summary>Every match, also those beyond the limit.</summary>
        public int TotalHits { get; internal set; }

        /// <summary>Records with at least one match.</summary>
        public int RecordCount { get; internal set; }

        /// <summary>Tables with at least one match.</summary>
        public int TableCount { get; internal set; }

        public bool IsTruncated => TotalHits > Hits.Count;
    }

    /// <summary>
    /// Finds a value in every member of every registered table: the current values (overrides included), list elements,
    /// members of nested objects and dictionary keys / values, compared as the text the debugger shows (case insensitive).
    /// </summary>
    public static class MasterMemoryGlobalSearch
    {
        public const int DefaultMaxHits = 500;

        const int MaxDepth = 4;
        const int MaxItems = 1000;

        sealed class Context
        {
            public string Query;
            public bool WholeValue;
            public int MaxHits;
            public MasterMemorySearchResult Result;
            public MasterMemoryRecordDescriptor Record;
            public MasterMemoryFieldDescriptor Field;
            public bool RecordMatched;
        }

        /// <param name="text">The text to find (trimmed). Empty text finds nothing.</param>
        /// <param name="wholeValue">Only values whose whole text equals <paramref name="text"/> ("1001" does not find 11001).</param>
        /// <param name="maxHits">Hits kept in <see cref="MasterMemorySearchResult.Hits"/>; every match is counted.</param>
        public static MasterMemorySearchResult Find(string text, bool wholeValue = false, int maxHits = DefaultMaxHits)
        {
            var result = new MasterMemorySearchResult();
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || !MasterMemoryDebugBuild.IsEnabled) return result;

            var context = new Context { Query = text, WholeValue = wholeValue, MaxHits = Math.Max(0, maxHits), Result = result };
            foreach (var table in MasterMemoryDebugRegistry.Tables)
            {
                var before = result.RecordCount;
                foreach (var record in table.CreateRecordSnapshot())
                {
                    context.Record = record;
                    context.RecordMatched = false;
                    var current = record.Current;
                    foreach (var field in table.TypeDescriptor.Fields)
                    {
                        context.Field = field;
                        object value;
                        try
                        {
                            value = field.GetValue(current);
                        }
                        catch (Exception)
                        {
                            continue;
                        }
                        Walk(value, string.Empty, 0, context);
                    }
                    if (context.RecordMatched) result.RecordCount++;
                }
                if (result.RecordCount > before) result.TableCount++;
            }
            return result;
        }

        static void Walk(object value, string path, int depth, Context context)
        {
            if (value == null) return;
            if (value is string s)
            {
                Match(s, value, path, context);
                return;
            }

            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || MasterDataValueUtility.GetKind(type) != MasterDataValueKind.Complex || depth >= MaxDepth)
            {
                Match(MasterDataValueUtility.Format(value), value, path, context);
                return;
            }

            switch (value)
            {
                case IDictionary dictionary:
                {
                    var count = 0;
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (count++ == MaxItems) break;
                        var entryPath = path + "[" + MasterDataValueUtility.Format(entry.Key) + "]";
                        Walk(entry.Key, entryPath, depth + 1, context);
                        Walk(entry.Value, entryPath, depth + 1, context);
                    }
                    return;
                }
                case IEnumerable enumerable:
                {
                    var index = 0;
                    foreach (var item in enumerable)
                    {
                        if (index == MaxItems) break;
                        Walk(item, path + "[" + index + "]", depth + 1, context);
                        index++;
                    }
                    return;
                }
            }

            if (MasterDataValueUtility.IsEditableObject(type))
            {
                foreach (var member in MasterDataReflectionCache.Get(type).Fields)
                {
                    if (member.HasSetter) Walk(member.GetValue(value), path + "." + member.Name, depth + 1, context);
                }
                return;
            }
            Match(MasterDataValueUtility.Format(value), value, path, context);
        }

        static void Match(string text, object value, string path, Context context)
        {
            if (text == null) return;
            var matches = context.WholeValue
                ? string.Equals(text, context.Query, StringComparison.OrdinalIgnoreCase)
                : text.IndexOf(context.Query, StringComparison.OrdinalIgnoreCase) >= 0;
            if (!matches) return;

            context.RecordMatched = true;
            var result = context.Result;
            result.TotalHits++;
            if (result.Hits.Count < context.MaxHits) result.Hits.Add(new MasterMemorySearchHit(context.Record, context.Field, path, value));
        }
    }
}
