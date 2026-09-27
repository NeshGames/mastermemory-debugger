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
        /// <summary>A deleted record whose original no longer exists (for example it was removed from the master data).</summary>
        OriginalMissing,
        /// <summary>The record only exists as an override (added in the debugger or by a patch).</summary>
        Added,
        /// <summary>The original record is deleted.</summary>
        Deleted,
    }

    /// <summary>One overridden record and its changed fields.</summary>
    public sealed class MasterMemoryChangeEntry
    {
        internal MasterMemoryChangeEntry(MasterDataOverrideEntry entry, MasterMemoryTableDescriptor table, object original, MasterMemoryChangeStatus status)
        {
            RecordType = entry.Key.RecordType;
            PrimaryKey = entry.Key.PrimaryKey;
            // a deleted record shows its original
            Current = entry.IsDeleted ? original : entry.Value;
            Table = table;
            Original = original;
            Status = status;
            Changes = new List<MasterDataFieldChange>();
            if (status == MasterMemoryChangeStatus.Changed)
            {
                Changes = MasterDataDiffUtility.GetChanges(original, entry.Value);
            }
            else if (status == MasterMemoryChangeStatus.Added)
            {
                // every value of the new record (keys are in the key column)
                foreach (var field in table.TypeDescriptor.Fields)
                {
                    if (!field.IsPrimaryKey) Changes.Add(new MasterDataFieldChange(field, null, field.GetValue(entry.Value)));
                }
            }
        }

        public Type RecordType { get; }
        public object PrimaryKey { get; }
        public string KeyText => MasterDataValueUtility.FormatKey(PrimaryKey);

        /// <summary>Null when <see cref="Status"/> is <see cref="MasterMemoryChangeStatus.TableNotRegistered"/>.</summary>
        public MasterMemoryTableDescriptor Table { get; }

        public string TableName => Table?.TableName ?? RecordType.Name;
        /// <summary>Null for an added record.</summary>
        public object Original { get; }

        /// <summary>The override; the original for a deleted record; null when a deleted record has no original.</summary>
        public object Current { get; }
        public MasterMemoryChangeStatus Status { get; }
        /// <summary>Changed fields; every non-key field of an added record (old value null); empty for a deleted record.</summary>
        public List<MasterDataFieldChange> Changes { get; }

        public bool IsAdded => Status == MasterMemoryChangeStatus.Added;

        public bool IsDeleted => Status == MasterMemoryChangeStatus.Deleted || Status == MasterMemoryChangeStatus.OriginalMissing;
        public string DisplayName => Table?.GetDisplayName(Current);
    }

    /// <summary>Every override with its changed fields, for the Changes panel and for tooling.</summary>
    public static class MasterMemoryChangeSummary
    {
        /// <summary>The field column of a deleted record in <see cref="ToTsv"/>.</summary>
        public const string DeletedField = "(deleted)";

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
                        var hasOriginal = originals.TryGetValue(entry.Key.PrimaryKey, out var original);
                        var status = entry.IsDeleted
                            ? hasOriginal ? MasterMemoryChangeStatus.Deleted : MasterMemoryChangeStatus.OriginalMissing
                            : hasOriginal ? MasterMemoryChangeStatus.Changed : MasterMemoryChangeStatus.Added;
                        result.Add(new MasterMemoryChangeEntry(entry, table, original, status));
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
        /// An added record has a line per field with an empty original; a deleted record one line with the field
        /// <see cref="DeletedField"/> (Paste TSV ignores it).
        /// Tabs and line breaks inside values become spaces.
        /// </summary>
        public static string ToTsv(IEnumerable<MasterMemoryChangeEntry> entries)
        {
            var text = new StringBuilder();
            text.Append("table\tkey\tname\tfield\toriginal\tcurrent\n");
            foreach (var entry in entries)
            {
                if (entry.Status == MasterMemoryChangeStatus.Deleted)
                {
                    text.Append(Cell(entry.TableName)).Append('\t')
                        .Append(Cell(entry.KeyText)).Append('\t')
                        .Append(Cell(entry.DisplayName)).Append('\t')
                        .Append(DeletedField).Append("\t\t\n");
                    continue;
                }
                foreach (var change in entry.Changes)
                {
                    text.Append(Cell(entry.TableName)).Append('\t')
                        .Append(Cell(entry.KeyText)).Append('\t')
                        .Append(Cell(entry.DisplayName)).Append('\t')
                        .Append(Cell(change.Name)).Append('\t')
                        .Append(entry.IsAdded ? string.Empty : Cell(MasterDataValueUtility.Format(change.OldValue))).Append('\t')
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

        internal static int CompareKeys(object a, object b)
        {
            if (a is IComparable comparable && a.GetType() == b?.GetType()) return comparable.CompareTo(b);
            return string.CompareOrdinal(MasterDataValueUtility.FormatKey(a), MasterDataValueUtility.FormatKey(b));
        }
    }
}
