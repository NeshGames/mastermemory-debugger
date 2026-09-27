using System;
using System.Collections.Generic;

namespace Nesh.MasterMemoryDebugger
{
    public interface IMasterDataOverrideStore
    {
        bool TryGet<TRecord, TKey>(TKey key, out TRecord value);

        void Set<TRecord, TKey>(TKey key, TRecord value);

        bool Remove<TRecord, TKey>(TKey key);

        bool IsOverridden<TRecord, TKey>(TKey key);

        /// <summary>True when the record is deleted (<see cref="Delete"/>); <see cref="TryGet{TRecord,TKey}"/> returns false for it.</summary>
        bool IsDeleted<TRecord, TKey>(TKey key);

        void Clear();

        int Count { get; }

        // Non generic API used by the debugger UI and the patch system.

        bool TryGet(Type recordType, object key, out object value);

        void Set(Type recordType, object key, object value);

        bool Remove(Type recordType, object key);

        /// <summary>
        /// Marks an original record as deleted: a rebuilt database (<see cref="MasterMemoryDebugRebuild"/>) leaves it out.
        /// <c>TryGet</c> returns false for it, so code reading through the store keeps seeing the original.
        /// <see cref="Remove"/> restores it. To delete a record added as an override, Remove that override.
        /// </summary>
        void Delete(Type recordType, object key);

        bool IsDeleted(Type recordType, object key);

        bool IsOverridden(Type recordType, object key);

        /// <summary>Number of overrides of a record type.</summary>
        int CountOf(Type recordType);

        /// <summary>
        /// Snapshot of all overrides, optionally filtered by record type. Deletions are included
        /// (<see cref="MasterDataOverrideEntry.IsDeleted"/>).
        /// </summary>
        List<MasterDataOverrideEntry> GetEntries(Type recordType = null);

        /// <summary>Raised after any change. Not raised when nothing changed.</summary>
        event Action Changed;
    }
}
