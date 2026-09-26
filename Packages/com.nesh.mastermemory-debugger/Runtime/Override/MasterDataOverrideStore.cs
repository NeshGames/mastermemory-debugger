using System;
using System.Collections.Generic;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Thread-safe in-memory override store.
    /// Lookups for record types without overrides return immediately without boxing the key.
    /// </summary>
    public sealed class MasterDataOverrideStore : IMasterDataOverrideStore
    {
        readonly Dictionary<MasterDataOverrideKey, object> overrides = new Dictionary<MasterDataOverrideKey, object>();
        readonly Dictionary<Type, int> countByType = new Dictionary<Type, int>();
        readonly object gate = new object();
        int batchDepth;
        bool changedInBatch;

        public event Action Changed;

        /// <summary>
        /// Raised for every changed record, before <see cref="Changed"/>: the key, the previous override (null when there
        /// was none) and the new one (null when removed). Used by the undo history.
        /// </summary>
        internal event Action<MasterDataOverrideKey, object, object> EntryChanged;

        public int Count
        {
            get
            {
                lock (gate) return overrides.Count;
            }
        }

        // ------------------------------------------------------------------ generic

        public bool TryGet<TRecord, TKey>(TKey key, out TRecord value)
        {
            lock (gate)
            {
                if (key != null && countByType.ContainsKey(typeof(TRecord))
                    && overrides.TryGetValue(new MasterDataOverrideKey(typeof(TRecord), key), out var boxed))
                {
                    value = (TRecord)boxed;
                    return true;
                }
            }
            value = default;
            return false;
        }

        public void Set<TRecord, TKey>(TKey key, TRecord value) => Set(typeof(TRecord), key, value);

        public bool Remove<TRecord, TKey>(TKey key) => Remove(typeof(TRecord), key);

        public bool IsOverridden<TRecord, TKey>(TKey key)
        {
            lock (gate)
            {
                return key != null && countByType.ContainsKey(typeof(TRecord))
                    && overrides.ContainsKey(new MasterDataOverrideKey(typeof(TRecord), key));
            }
        }

        // ------------------------------------------------------------------ non generic

        public bool TryGet(Type recordType, object key, out object value)
        {
            lock (gate)
            {
                if (key != null && countByType.ContainsKey(recordType))
                {
                    return overrides.TryGetValue(new MasterDataOverrideKey(recordType, key), out value);
                }
            }
            value = null;
            return false;
        }

        public void Set(Type recordType, object key, object value)
        {
            if (recordType == null) throw new ArgumentNullException(nameof(recordType));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (value == null) throw new ArgumentNullException(nameof(value), "Use Remove to delete an override. Records can not be deleted.");
            if (!recordType.IsInstanceOfType(value))
            {
                throw new ArgumentException($"Value type {value.GetType().FullName} is not assignable to {recordType.FullName}.", nameof(value));
            }

            var overrideKey = new MasterDataOverrideKey(recordType, key);
            object before;
            lock (gate)
            {
                if (!overrides.TryGetValue(overrideKey, out before))
                {
                    countByType.TryGetValue(recordType, out var count);
                    countByType[recordType] = count + 1;
                }
                overrides[overrideKey] = value;
            }
            EntryChanged?.Invoke(overrideKey, before, value);
            RaiseChanged();
        }

        public bool Remove(Type recordType, object key)
        {
            if (recordType == null || key == null) return false;
            var overrideKey = new MasterDataOverrideKey(recordType, key);
            object before;
            lock (gate)
            {
                if (!overrides.TryGetValue(overrideKey, out before)) return false;
                overrides.Remove(overrideKey);
                var count = countByType[recordType] - 1;
                if (count == 0) countByType.Remove(recordType);
                else countByType[recordType] = count;
            }
            EntryChanged?.Invoke(overrideKey, before, null);
            RaiseChanged();
            return true;
        }

        public bool IsOverridden(Type recordType, object key)
        {
            lock (gate)
            {
                return key != null && countByType.ContainsKey(recordType)
                    && overrides.ContainsKey(new MasterDataOverrideKey(recordType, key));
            }
        }

        public int CountOf(Type recordType)
        {
            lock (gate)
            {
                return countByType.TryGetValue(recordType, out var count) ? count : 0;
            }
        }

        public List<MasterDataOverrideEntry> GetEntries(Type recordType = null)
        {
            lock (gate)
            {
                var list = new List<MasterDataOverrideEntry>(recordType == null ? overrides.Count : CountOfNoLock(recordType));
                foreach (var pair in overrides)
                {
                    if (recordType == null || pair.Key.RecordType == recordType)
                    {
                        list.Add(new MasterDataOverrideEntry(pair.Key, pair.Value));
                    }
                }
                return list;
            }
        }

        public void Clear()
        {
            List<KeyValuePair<MasterDataOverrideKey, object>> removed;
            lock (gate)
            {
                if (overrides.Count == 0) return;
                removed = new List<KeyValuePair<MasterDataOverrideKey, object>>(overrides);
                overrides.Clear();
                countByType.Clear();
            }
            var entryChanged = EntryChanged;
            if (entryChanged != null)
            {
                foreach (var pair in removed) entryChanged(pair.Key, pair.Value, null);
            }
            RaiseChanged();
        }

        /// <summary>
        /// Suppresses <see cref="Changed"/> until the returned scope is disposed, then raises it once if anything changed.
        /// </summary>
        public IDisposable BeginBatch()
        {
            lock (gate) batchDepth++;
            return new BatchScope(this);
        }

        int CountOfNoLock(Type recordType) => countByType.TryGetValue(recordType, out var count) ? count : 0;

        void RaiseChanged()
        {
            lock (gate)
            {
                if (batchDepth > 0)
                {
                    changedInBatch = true;
                    return;
                }
            }
            Changed?.Invoke();
        }

        void EndBatch()
        {
            bool raise;
            lock (gate)
            {
                batchDepth--;
                raise = batchDepth == 0 && changedInBatch;
                if (batchDepth == 0) changedInBatch = false;
            }
            if (raise) Changed?.Invoke();
        }

        sealed class BatchScope : IDisposable
        {
            MasterDataOverrideStore owner;

            public BatchScope(MasterDataOverrideStore owner)
            {
                this.owner = owner;
            }

            public void Dispose()
            {
                owner?.EndBatch();
                owner = null;
            }
        }
    }
}
