using System;
using System.Text;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// One record of a table (an original one, or one added as an override: <see cref="IsAdded"/>) plus cached text used
    /// by the record list and search.
    /// </summary>
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

        /// <summary>The record owned by the MasterMemory database. Never modify it. Null for an added record.</summary>
        public object Original { get; }

        /// <summary>True for a record that only exists as an override (added in the debugger or by a patch).</summary>
        public bool IsAdded => Original == null;

        /// <summary>True when the original record is deleted (<see cref="IMasterDataOverrideStore.Delete"/>).</summary>
        public bool IsDeleted => MasterMemoryDebugRuntime.Store.IsDeleted(Table.RecordType, PrimaryKey);

        public object PrimaryKey { get; }

        public string KeyText => keyText ?? (keyText = MasterDataValueUtility.FormatKey(PrimaryKey));

        public bool IsModified => MasterMemoryDebugRuntime.Store.IsOverridden(Table.RecordType, PrimaryKey);

        /// <summary>
        /// The override when present, otherwise the original (also for a deleted record). Null for an added record whose
        /// override was removed since the snapshot was taken.
        /// </summary>
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
            if (current == null) return false;
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
