using System;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Composition root for one debugger runtime. Public APIs remain static facades for easy game integration, while
    /// mutable services are owned by this session so their lifetime and dependencies are explicit.
    /// </summary>
    public sealed class MasterMemoryDebugSession
    {
        internal MasterMemoryDebugSession(MasterDataOverrideStore overrideStore, IMasterMemoryAdapter masterMemoryAdapter)
        {
            OverrideStore = overrideStore ?? throw new ArgumentNullException(nameof(overrideStore));
            MasterMemoryAdapter = masterMemoryAdapter ?? throw new ArgumentNullException(nameof(masterMemoryAdapter));
        }

        /// <summary>The override store owned by this runtime session.</summary>
        public IMasterDataOverrideStore Store => OverrideStore;

        internal MasterDataOverrideStore OverrideStore { get; }

        internal IMasterMemoryAdapter MasterMemoryAdapter { get; }

        internal static MasterMemoryDebugSession CreateDefault() =>
            new MasterMemoryDebugSession(new MasterDataOverrideStore(), new MasterMemoryV3Adapter());
    }
}
