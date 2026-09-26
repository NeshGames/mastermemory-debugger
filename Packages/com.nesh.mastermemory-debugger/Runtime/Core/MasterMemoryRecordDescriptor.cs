using System;
using System.Text;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>One original record of a table plus cached text used by the record list and search.</summary>
    public sealed class MasterMemoryRecordDescriptor
    {
        string keyText;
        string searchText;

        internal MasterMemoryRecordDescriptor(MasterMemoryTableDescriptor table, object original, object primaryKey)
        {
            Table = table;
            Original = original;
            PrimaryKey = primaryKey;
        }

        public MasterMemoryTableDescriptor Table { get; }

        /// <summary>The record owned by the MasterMemory database. Never modify it.</summary>
        public object Original { get; }

        public object PrimaryKey { get; }

        public string KeyText => keyText ?? (keyText = MasterDataValueUtility.FormatKey(PrimaryKey));

        public bool IsModified => MasterMemoryDebugRuntime.Store.IsOverridden(Table.RecordType, PrimaryKey);

        /// <summary>The override when present, otherwise the original.</summary>
        public object Current => MasterMemoryDebugRuntime.Store.TryGet(Table.RecordType, PrimaryKey, out var value) ? value : Original;

        public string GetDisplayName() => Table.GetDisplayName(Current);

        /// <summary>
        /// Returns true when the query matches the primary key, the display name or any string member.
        /// String member text is cached, the display name / overrides are evaluated every call.
        /// </summary>
        public bool Matches(string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            if (KeyText.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            var current = Current;
            var displayName = Table.GetDisplayName(current);
            if (displayName != null && displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            var text = ReferenceEquals(current, Original) ? GetOriginalSearchText() : BuildSearchText(current);
            return text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        string GetOriginalSearchText() => searchText ?? (searchText = BuildSearchText(Original));

        string BuildSearchText(object record)
        {
            var fields = Table.TypeDescriptor.StringFields;
            if (fields.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            foreach (var field in fields)
            {
                if (field.GetValue(record) is string s) sb.Append(s).Append('\n');
            }
            return sb.ToString();
        }
    }
}
