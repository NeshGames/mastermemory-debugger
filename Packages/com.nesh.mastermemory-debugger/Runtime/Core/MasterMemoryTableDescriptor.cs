using System;
using System.Collections.Generic;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>A table registered to the debugger.</summary>
    public sealed class MasterMemoryTableDescriptor
    {
        readonly Func<object, string> getDisplayName;
        readonly bool customDisplayName;

        internal MasterMemoryTableDescriptor(
            string tableName,
            string memoryTableName,
            Type recordType,
            Type keyType,
            Func<IEnumerable<object>> getAllRecords,
            Func<object, object> getPrimaryKey,
            Func<object, string> getDisplayName,
            bool customDisplayName = false)
        {
            if (string.IsNullOrEmpty(tableName)) throw new ArgumentException("Table name is required.", nameof(tableName));
            TableName = tableName;
            MemoryTableName = memoryTableName;
            RecordType = recordType ?? throw new ArgumentNullException(nameof(recordType));
            KeyType = keyType ?? throw new ArgumentNullException(nameof(keyType));
            GetAllRecords = getAllRecords ?? throw new ArgumentNullException(nameof(getAllRecords));
            GetPrimaryKey = getPrimaryKey ?? throw new ArgumentNullException(nameof(getPrimaryKey));
            this.getDisplayName = getDisplayName;
            this.customDisplayName = customDisplayName;
            TypeDescriptor = MasterDataReflectionCache.Get(recordType);
        }

        /// <summary>Name shown in the UI and written to patches (for example "SkillMaster").</summary>
        public string TableName { get; }

        /// <summary>Name given to <c>[MemoryTable]</c>, when known.</summary>
        public string MemoryTableName { get; }

        public Type RecordType { get; }

        /// <summary>Primary key type. Composite keys are ValueTuples.</summary>
        public Type KeyType { get; }

        /// <summary>Returns the ORIGINAL (not overridden) records.</summary>
        public Func<IEnumerable<object>> GetAllRecords { get; }

        public Func<object, object> GetPrimaryKey { get; }

        public MasterDataTypeDescriptor TypeDescriptor { get; }

        public bool HasDisplayName => getDisplayName != null || MasterMemoryDebugRegistry.HasDisplayNameOverride(RecordType);

        /// <summary>
        /// True when the project supplied the display name (RegisterTable / SetDisplayName); the default one only repeats a member.
        /// </summary>
        public bool HasCustomDisplayName => customDisplayName || MasterMemoryDebugRegistry.HasDisplayNameOverride(RecordType);

        public string GetDisplayName(object record)
        {
            if (record == null) return null;
            var custom = MasterMemoryDebugRegistry.GetDisplayNameOverride(RecordType);
            if (custom != null) return custom(record);
            return getDisplayName?.Invoke(record);
        }

        /// <summary>
        /// Takes a snapshot of the records: the originals (deleted ones included, see
        /// <see cref="MasterMemoryRecordDescriptor.IsDeleted"/>), then the records added as overrides, by key.
        /// </summary>
        public List<MasterMemoryRecordDescriptor> CreateRecordSnapshot()
        {
            var list = new List<MasterMemoryRecordDescriptor>();
            var records = GetAllRecords();
            if (records != null)
            {
                foreach (var record in records)
                {
                    if (record == null) continue;
                    list.Add(new MasterMemoryRecordDescriptor(this, record, GetPrimaryKey(record)));
                }
            }

            var store = MasterMemoryDebugRuntime.Store;
            if (store.CountOf(RecordType) == 0) return list;
            HashSet<object> originalKeys = null;
            List<MasterDataOverrideEntry> added = null;
            foreach (var entry in store.GetEntries(RecordType))
            {
                if (entry.IsDeleted) continue;
                if (originalKeys == null)
                {
                    originalKeys = new HashSet<object>();
                    foreach (var record in list) originalKeys.Add(record.PrimaryKey);
                }
                if (originalKeys.Contains(entry.Key.PrimaryKey)) continue;
                (added ??= new List<MasterDataOverrideEntry>()).Add(entry);
            }
            if (added == null) return list;
            added.Sort((a, b) => MasterMemoryChangeSummary.CompareKeys(a.Key.PrimaryKey, b.Key.PrimaryKey));
            foreach (var entry in added) list.Add(new MasterMemoryRecordDescriptor(this, null, entry.Key.PrimaryKey));
            return list;
        }

        /// <summary>Finds an original record by primary key. O(n); intended for tooling, not gameplay.</summary>
        public bool TryFindOriginal(object key, out object record)
        {
            if (key != null)
            {
                foreach (var item in GetAllRecords())
                {
                    if (item != null && Equals(GetPrimaryKey(item), key))
                    {
                        record = item;
                        return true;
                    }
                }
            }
            record = null;
            return false;
        }

        public override string ToString() => TableName;
    }
}
