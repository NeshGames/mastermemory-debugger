using System;
using System.Collections.Generic;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Thread-safe in-memory override store.
    /// Lookups for record types without overrides return immediately without boxing the key.
    /// An override whose key has no original record adds a record; <see cref="Deleted"/> as the value deletes one.
    /// </summary>
    public sealed class MasterDataOverrideStore : IMasterDataOverrideStore
    {
        sealed class DeletedMarker
        {
            public override string ToString() => "(deleted)";
        }

        /// <summary>The value of a deleted record in <see cref="GetEntries"/> and in the undo history.</summary>
        public static readonly object Deleted = new DeletedMarker();

        public static bool IsDeletedValue(object value) => ReferenceEquals(value, Deleted);

        readonly Dictionary<MasterDataOverrideKey, object> overrides = new Dictionary<MasterDataOverrideKey, object>();
        readonly Dictionary<Type, int> countByType = new Dictionary<Type, int>();
        readonly object gate = new object();
        int batchDepth;
        bool changedInBatch;

        public event Action Changed;

        /// <summary>
        /// Raised for every changed record, before <see cref="Changed"/>: the key, the previous override (null when there
        /// was none) and the new one (null when removed); <see cref="Deleted"/> for a deletion. Used by the undo history.
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
                    && overrides.TryGetValue(new MasterDataOverrideKey(typeof(TRecord), key), out var boxed)
                    && !IsDeletedValue(boxed))
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

        public bool IsDeleted<TRecord, TKey>(TKey key)
        {
            lock (gate)
            {
                return key != null && countByType.ContainsKey(typeof(TRecord))
                    && overrides.TryGetValue(new MasterDataOverrideKey(typeof(TRecord), key), out var value) && IsDeletedValue(value);
            }
        }

        // ------------------------------------------------------------------ non generic

        /// <summary>The overriding record; false when there is none or the record is deleted.</summary>
        public bool TryGet(Type recordType, object key, out object value)
        {
            lock (gate)
            {
                if (key != null && countByType.ContainsKey(recordType)
                    && overrides.TryGetValue(new MasterDataOverrideKey(recordType, key), out value) && !IsDeletedValue(value))
                {
                    return true;
                }
            }
            value = null;
            return false;
        }

        public void Delete(Type recordType, object key) => Set(recordType, key, Deleted);

        public bool IsDeleted(Type recordType, object key)
        {
            lock (gate)
            {
                return key != null && countByType.ContainsKey(recordType)
                    && overrides.TryGetValue(new MasterDataOverrideKey(recordType, key), out var value) && IsDeletedValue(value);
            }
        }

        public void Set(Type recordType, object key, object value)
        {
            if (recordType == null) throw new ArgumentNullException(nameof(recordType));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (value == null) throw new ArgumentNullException(nameof(value), "Use Remove to remove an override, Delete to delete a record.");
            if (!IsDeletedValue(value) && !recordType.IsInstanceOfType(value))
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

        internal sealed class AtomicChange
        {
            public Type RecordType;
            public object Key;
            /// <summary>Null removes, Deleted deletes, otherwise sets the override.</summary>
            public object Value;
            public object ExpectedValue;
        }

        /// <summary>Commits a fully validated set in one store update and emits change notifications afterwards.</summary>
        internal void ApplyAtomic(IReadOnlyList<AtomicChange> changes)
        {
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            foreach (var change in changes)
            {
                if (change.RecordType == null || change.Key == null)
                    throw new ArgumentException("Every atomic change needs a type and key.");
                if (change.Value != null && !IsDeletedValue(change.Value)
                    && !change.RecordType.IsInstanceOfType(change.Value))
                    throw new ArgumentException("Atomic change has an incompatible value.");
            }

            var events = new List<Tuple<MasterDataOverrideKey, object, object>>(changes.Count);
            lock (gate)
            {
                foreach (var change in changes)
                {
                    overrides.TryGetValue(new MasterDataOverrideKey(change.RecordType, change.Key), out var before);
                    if (!ReferenceEquals(before, change.ExpectedValue))
                        throw new InvalidOperationException("A target override changed during Patch preflight.");
                }
                foreach (var change in changes)
                {
                    var key = new MasterDataOverrideKey(change.RecordType, change.Key);
                    overrides.TryGetValue(key, out var before);
                    if (change.Value == null)
                    {
                        if (before == null) continue;
                        overrides.Remove(key);
                        var count = countByType[change.RecordType] - 1;
                        if (count == 0) countByType.Remove(change.RecordType);
                        else countByType[change.RecordType] = count;
                    }
                    else
                    {
                        if (before == null)
                        {
                            countByType.TryGetValue(change.RecordType, out var count);
                            countByType[change.RecordType] = count + 1;
                        }
                        overrides[key] = change.Value;
                    }
                    events.Add(Tuple.Create(key, before, change.Value));
                }
            }

            var handlers = EntryChanged?.GetInvocationList();
            foreach (var item in events)
            {
                if (handlers == null) break;
                foreach (Action<MasterDataOverrideKey, object, object> handler in handlers)
                {
                    try { handler(item.Item1, item.Item2, item.Item3); }
                    catch (Exception e) { MasterMemoryDebugLog.Warning("Atomic override notification failed: " + e.Message); }
                }
            }
            if (events.Count == 0) return;
            try { RaiseChanged(); }
            catch (Exception e) { MasterMemoryDebugLog.Warning("Atomic override change notification failed: " + e.Message); }
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
