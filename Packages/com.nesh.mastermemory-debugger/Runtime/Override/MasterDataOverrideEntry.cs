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

        /// <summary>The overriding record. Never an instance owned by the MasterMemory database.</summary>
        public object Value { get; }
    }
}
