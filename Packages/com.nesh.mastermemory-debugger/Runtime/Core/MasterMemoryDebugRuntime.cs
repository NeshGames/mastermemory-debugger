using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Entry point used by the game's master data access layer.
    /// <code>
    /// public SkillMaster GetSkill(int id)
    /// {
    ///     if (MasterMemoryDebugRuntime.TryGetOverride&lt;SkillMaster, int&gt;(id, out var value)) return value;
    ///     return _database.SkillMasterTable.FindById(id);
    /// }
    /// </code>
    /// Outside the Editor / Development Builds every lookup returns false and every mutation is ignored,
    /// so call sites need no <c>#if</c>.
    /// </summary>
    public static class MasterMemoryDebugRuntime
    {
        static MasterDataOverrideStore s_store = CreateStore();

        /// <summary>Raised after overrides were applied, reset or loaded from a patch.</summary>
        public static event Action OverridesChanged;

        /// <summary>Every changed record: key, previous override (or null), new override (or null when removed). Remote sync.</summary>
        internal static event Action<MasterDataOverrideKey, object, object> EntryChanged;

        /// <summary>The override store used by the debugger.</summary>
        public static IMasterDataOverrideStore Store => s_store;

        public static bool IsEnabled => MasterMemoryDebugBuild.IsEnabled;

        public static int OverrideCount => MasterMemoryDebugBuild.IsEnabled ? s_store.Count : 0;

        public static bool TryGetOverride<TRecord, TKey>(TKey key, out TRecord value)
        {
            if (!MasterMemoryDebugBuild.IsEnabled)
            {
                value = default;
                return false;
            }
            return s_store.TryGet(key, out value);
        }

        /// <summary>Returns the override when present, otherwise <paramref name="fallback"/>(key).</summary>
        public static TRecord Resolve<TRecord, TKey>(TKey key, Func<TKey, TRecord> fallback)
        {
            if (TryGetOverride<TRecord, TKey>(key, out var value)) return value;
            return fallback(key);
        }

        /// <summary>
        /// Registers an override. <paramref name="value"/> must be a copy, never the instance owned by MasterMemory
        /// (use <see cref="MasterDataCloneUtility.Clone{T}"/>), and its primary key must equal <paramref name="key"/>.
        /// </summary>
        public static void SetOverride<TRecord, TKey>(TKey key, TRecord value)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return;
            s_store.Set(key, value);
        }

        public static bool RemoveOverride<TRecord, TKey>(TKey key)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return false;
            return s_store.Remove<TRecord, TKey>(key);
        }

        public static bool IsOverridden<TRecord, TKey>(TKey key)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return false;
            return s_store.IsOverridden<TRecord, TKey>(key);
        }

        public static void ClearAllOverrides()
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return;
            s_store.Clear();
        }

        /// <summary>
        /// All overriding records of a type. Useful to rebuild a database in the project:
        /// <c>builder.Diff(MasterMemoryDebugRuntime.GetOverrides&lt;SkillMaster&gt;())</c>.
        /// </summary>
        public static TRecord[] GetOverrides<TRecord>()
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return Array.Empty<TRecord>();
            var entries = s_store.GetEntries(typeof(TRecord));
            var result = new TRecord[entries.Count];
            for (var i = 0; i < entries.Count; i++) result[i] = (TRecord)entries[i].Value;
            return result;
        }

        /// <summary>Snapshot of every override.</summary>
        public static List<MasterDataOverrideEntry> GetAllOverrides()
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return new List<MasterDataOverrideEntry>();
            return s_store.GetEntries();
        }

        /// <summary>Groups several changes so that <see cref="OverridesChanged"/> is raised only once.</summary>
        public static IDisposable BeginBatch() => s_store.BeginBatch();

        static MasterDataOverrideStore CreateStore()
        {
            var store = new MasterDataOverrideStore();
            store.EntryChanged += MasterMemoryDebugHistory.OnEntryChanged;
            store.EntryChanged += (key, before, after) => EntryChanged?.Invoke(key, before, after);
            store.Changed += RaiseOverridesChanged;
            return store;
        }

        static void RaiseOverridesChanged()
        {
            var handler = OverridesChanged;
            if (handler == null) return;
            try
            {
                handler();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_store = CreateStore();
            OverridesChanged = null;
            MasterMemoryDebugHistory.Clear();
        }
    }
}
