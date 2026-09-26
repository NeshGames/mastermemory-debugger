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

        void Clear();

        int Count { get; }

        // Non generic API used by the debugger UI and the patch system.

        bool TryGet(Type recordType, object key, out object value);

        void Set(Type recordType, object key, object value);

        bool Remove(Type recordType, object key);

        bool IsOverridden(Type recordType, object key);

        /// <summary>Number of overrides of a record type.</summary>
        int CountOf(Type recordType);

        /// <summary>Snapshot of all overrides, optionally filtered by record type.</summary>
        List<MasterDataOverrideEntry> GetEntries(Type recordType = null);

        /// <summary>Raised after any change. Not raised when nothing changed.</summary>
        event Action Changed;
    }
}
