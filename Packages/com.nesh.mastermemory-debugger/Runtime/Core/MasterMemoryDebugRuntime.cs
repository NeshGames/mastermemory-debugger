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
        static MasterMemoryDebugSession s_session = CreateSession();

        /// <summary>Raised after overrides were applied, reset or loaded from a patch.</summary>
        public static event Action OverridesChanged;

        /// <summary>Every changed record: key, previous override (or null), new override (or null when removed). Remote sync.</summary>
        internal static event Action<MasterDataOverrideKey, object, object> EntryChanged;

        /// <summary>The override store used by the debugger.</summary>
        public static IMasterDataOverrideStore Store => s_session.Store;

        /// <summary>The active debugger service lifetime. Static APIs delegate to this session.</summary>
        public static MasterMemoryDebugSession Session => s_session;

        public static bool IsEnabled => MasterMemoryDebugBuild.IsEnabled;

        public static int OverrideCount => MasterMemoryDebugBuild.IsEnabled ? s_session.OverrideStore.Count : 0;

        public static bool TryGetOverride<TRecord, TKey>(TKey key, out TRecord value)
        {
            if (!MasterMemoryDebugBuild.IsEnabled)
            {
                value = default;
                return false;
            }
            return s_session.OverrideStore.TryGet(key, out value);
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
            s_session.OverrideStore.Set(key, value);
        }

        public static bool RemoveOverride<TRecord, TKey>(TKey key)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return false;
            return s_session.OverrideStore.Remove<TRecord, TKey>(key);
        }

        public static bool IsOverridden<TRecord, TKey>(TKey key)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return false;
            return s_session.OverrideStore.IsOverridden<TRecord, TKey>(key);
        }

        /// <summary>
        /// True when the record was deleted in the debugger. Deletions take effect in a database rebuilt by
        /// <see cref="MasterMemoryDebugRebuild"/>; <see cref="TryGetOverride{TRecord,TKey}"/> / <see cref="Resolve{TRecord,TKey}"/>
        /// keep returning the original, so code that reads records by key can check this to honor a deletion.
        /// </summary>
        public static bool IsDeleted<TRecord, TKey>(TKey key)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return false;
            return s_session.OverrideStore.IsDeleted<TRecord, TKey>(key);
        }

        public static void ClearAllOverrides()
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return;
            s_session.OverrideStore.Clear();
        }

        /// <summary>
        /// All overriding records of a type (changed and added ones, not the deletions). Useful to rebuild a database in
        /// the project: <c>builder.Diff(MasterMemoryDebugRuntime.GetOverrides&lt;SkillMaster&gt;())</c>.
        /// </summary>
        public static TRecord[] GetOverrides<TRecord>()
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return Array.Empty<TRecord>();
            var entries = s_session.OverrideStore.GetEntries(typeof(TRecord));
            var result = new List<TRecord>(entries.Count);
            foreach (var entry in entries)
            {
                if (!entry.IsDeleted) result.Add((TRecord)entry.Value);
            }
            return result.ToArray();
        }

        /// <summary>Primary keys of the records of a type deleted in the debugger (see <see cref="IsDeleted{TRecord,TKey}"/>).</summary>
        public static TKey[] GetDeletedKeys<TRecord, TKey>()
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return Array.Empty<TKey>();
            var result = new List<TKey>();
            foreach (var entry in s_session.OverrideStore.GetEntries(typeof(TRecord)))
            {
                if (entry.IsDeleted) result.Add((TKey)entry.Key.PrimaryKey);
            }
            return result.ToArray();
        }

        /// <summary>Snapshot of every override, deletions included (<see cref="MasterDataOverrideEntry.IsDeleted"/>).</summary>
        public static List<MasterDataOverrideEntry> GetAllOverrides()
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return new List<MasterDataOverrideEntry>();
            return s_session.OverrideStore.GetEntries();
        }

        /// <summary>Groups several changes so that <see cref="OverridesChanged"/> is raised only once.</summary>
        public static IDisposable BeginBatch() => s_session.OverrideStore.BeginBatch();

        static MasterMemoryDebugSession CreateSession()
        {
            var session = MasterMemoryDebugSession.CreateDefault();
            var store = session.OverrideStore;
            store.EntryChanged += MasterMemoryDebugHistory.OnEntryChanged;
            store.EntryChanged += (key, before, after) => EntryChanged?.Invoke(key, before, after);
            store.Changed += RaiseOverridesChanged;
            return session;
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
            s_session = CreateSession();
            OverridesChanged = null;
            MasterMemoryDebugHistory.Clear();
        }
    }
}
