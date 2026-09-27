namespace Nesh.MasterMemoryDebugger
{
    /// <summary>A record replacement stored in the override store.</summary>
    public readonly struct MasterDataOverrideEntry
    {
        public MasterDataOverrideEntry(MasterDataOverrideKey key, object value)
        {
            Key = key;
            Value = value;
        }

        public MasterDataOverrideKey Key { get; }

        /// <summary>
        /// The overriding record, never an instance owned by the MasterMemory database; <see cref="MasterDataOverrideStore.Deleted"/>
        /// for a deleted record.
        /// </summary>
        public object Value { get; }

        /// <summary>True when the entry deletes the record (<see cref="IMasterDataOverrideStore.Delete"/>).</summary>
        public bool IsDeleted => MasterDataOverrideStore.IsDeletedValue(Value);
    }
}
