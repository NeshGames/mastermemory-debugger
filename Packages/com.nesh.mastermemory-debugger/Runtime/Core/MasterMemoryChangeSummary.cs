using System;
using System.Collections.Generic;
using System.Text;

namespace Nesh.MasterMemoryDebugger
{
    public enum MasterMemoryChangeStatus
    {
        /// <summary>The override differs from its original record.</summary>
        Changed,
        /// <summary>The record type is not registered, so the original can not be looked up.</summary>
        TableNotRegistered,
        /// <summary>No original record with this primary key exists (for example it was removed from the master data).</summary>
        OriginalMissing,
    }

    /// <summary>One overridden record and its changed fields.</summary>
    public sealed class MasterMemoryChangeEntry
    {
        internal MasterMemoryChangeEntry(MasterDataOverrideEntry entry, MasterMemoryTableDescriptor table, object original, MasterMemoryChangeStatus status)
        {
            RecordType = entry.Key.RecordType;
            PrimaryKey = entry.Key.PrimaryKey;
            Current = entry.Value;
            Table = table;
            Original = original;
            Status = status;
            Changes = original != null ? MasterDataDiffUtility.GetChanges(original, entry.Value) : new List<MasterDataFieldChange>();
        }

        public Type RecordType { get; }
        public object PrimaryKey { get; }
        public string KeyText => MasterDataValueUtility.FormatKey(PrimaryKey);

        /// <summary>Null when <see cref="Status"/> is <see cref="MasterMemoryChangeStatus.TableNotRegistered"/>.</summary>
        public MasterMemoryTableDescriptor Table { get; }

        public string TableName => Table?.TableName ?? RecordType.Name;
        public object Original { get; }
        public object Current { get; }
        public MasterMemoryChangeStatus Status { get; }
        public List<MasterDataFieldChange> Changes { get; }
        public string DisplayName => Table?.GetDisplayName(Current);
    }

    /// <summary>Every override with its changed fields, for the Changes panel and for tooling.</summary>
    public static class MasterMemoryChangeSummary
    {
        /// <summary>
        /// Builds the list ordered like the table list (groups, then table name) and by primary key;
        /// overrides of unregistered types come last.
        /// </summary>
        public static List<MasterMemoryChangeEntry> Build()
        {
            var result = new List<MasterMemoryChangeEntry>();
            var entries = MasterMemoryDebugRuntime.GetAllOverrides();
            if (entries.Count == 0) return result;

            var handled = new HashSet<Type>();
            foreach (var group in MasterMemoryDebugRegistry.GetGroupedTables())
            {
                foreach (var table in group.Tables)
                {
                    handled.Add(table.RecordType);
                    var tableEntries = entries.FindAll(x => x.Key.RecordType == table.RecordType);
                    if (tableEntries.Count == 0) continue;

                    var originals = new Dictionary<object, object>();
                    foreach (var record in table.GetAllRecords())
                    {
                        if (record != null) originals[table.GetPrimaryKey(record)] = record;
                    }

                    tableEntries.Sort((a, b) => CompareKeys(a.Key.PrimaryKey, b.Key.PrimaryKey));
                    foreach (var entry in tableEntries)
                    {
                        result.Add(originals.TryGetValue(entry.Key.PrimaryKey, out var original)
                            ? new MasterMemoryChangeEntry(entry, table, original, MasterMemoryChangeStatus.Changed)
                            : new MasterMemoryChangeEntry(entry, table, null, MasterMemoryChangeStatus.OriginalMissing));
                    }
                }
            }

            foreach (var entry in entries)
            {
                if (!handled.Contains(entry.Key.RecordType))
                {
                    result.Add(new MasterMemoryChangeEntry(entry, null, null, MasterMemoryChangeStatus.TableNotRegistered));
                }
            }
            return result;
        }

        /// <summary>
        /// One tab separated line per changed field: table, key, display name, field, original, current (with a title line).
        /// For pasting into a spreadsheet, for example to copy tuned values back into the master data source.
        /// Tabs and line breaks inside values become spaces.
        /// </summary>
        public static string ToTsv(IEnumerable<MasterMemoryChangeEntry> entries)
        {
            var text = new StringBuilder();
            text.Append("table\tkey\tname\tfield\toriginal\tcurrent\n");
            foreach (var entry in entries)
            {
                foreach (var change in entry.Changes)
                {
                    text.Append(Cell(entry.TableName)).Append('\t')
                        .Append(Cell(entry.KeyText)).Append('\t')
                        .Append(Cell(entry.DisplayName)).Append('\t')
                        .Append(Cell(change.Name)).Append('\t')
                        .Append(Cell(MasterDataValueUtility.Format(change.OldValue))).Append('\t')
                        .Append(Cell(MasterDataValueUtility.Format(change.NewValue))).Append('\n');
                }
            }
            return text.ToString();
        }

        static string Cell(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        }

        static int CompareKeys(object a, object b)
        {
            if (a is IComparable comparable && a.GetType() == b?.GetType()) return comparable.CompareTo(b);
            return string.CompareOrdinal(MasterDataValueUtility.FormatKey(a), MasterDataValueUtility.FormatKey(b));
        }
    }
}
