using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// When <see cref="MasterMemoryDebuggerSettings.AutoLoadPatch"/> is on, applies the saved patch table by table
    /// as the game registers its tables. A patch made for another master version is never auto-loaded.
    /// </summary>
    internal static class MasterDataPatchAutoLoader
    {
        static bool s_loaded;
        static MasterDataPatch s_pending;
        static readonly HashSet<string> s_appliedTables = new HashSet<string>(StringComparer.Ordinal);

        public static void OnTableRegistered(MasterMemoryTableDescriptor table)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return;
            var settings = MasterMemoryDebuggerSettings.Current;
            if (!settings.Enabled || !settings.AutoLoadPatch) return;

            EnsureLoaded();
            if (s_pending == null) return;

            foreach (var patchTable in s_pending.Tables)
            {
                if (MasterDataPatchResolver.FindTable(patchTable) != table) continue;
                if (!s_appliedTables.Add(table.TableName)) return;

                // The full registry may not exist yet, so a saved SchemaHash cannot be compared here.
                // Keep the released table-by-table compatibility behavior, but route mutation through the shared engine.
                var tablePatch = new MasterDataPatch
                {
                    FormatVersion = s_pending.FormatVersion,
                    MasterVersion = s_pending.MasterVersion,
                    ExportedAt = s_pending.ExportedAt,
                };
                tablePatch.Tables.Add(patchTable);
                var plan = MasterDataPatchEngine.Build(
                    tablePatch,
                    replaceExisting: false,
                    requireOriginalPreconditions: false,
                    forceIdentity: false,
                    allowPartial: true);

                foreach (var warning in plan.Warnings) MasterMemoryDebugLog.Warning(warning);
                if (!plan.Succeeded)
                {
                    foreach (var error in plan.Errors)
                    {
                        MasterMemoryDebugLog.Warning(
                            $"Auto load {table.TableName}: {error.Code} {error.Key} {error.Field} {error.Message}".Trim());
                    }
                    return;
                }

                try
                {
                    MasterDataPatchEngine.Commit(plan);
                }
                catch (InvalidOperationException e)
                {
                    MasterMemoryDebugLog.Warning($"Auto load {table.TableName}: {e.Message}");
                    return;
                }

                MasterMemoryDebugLog.Info($"Auto loaded patch for {table.TableName}: {plan.Targets.Count} records.");
                return;
            }
        }

        static void EnsureLoaded()
        {
            if (s_loaded) return;
            s_loaded = true;
            try
            {
                var patch = MasterDataPatchStorage.Load();
                if (patch == null) return;
                if (patch.FormatVersion != MasterDataPatch.CurrentFormatVersion)
                {
                    MasterMemoryDebugLog.Warning($"Saved patch has unsupported format version {patch.FormatVersion}; auto load skipped.");
                    return;
                }
                var current = MasterMemoryDebugRegistry.GetMasterVersion();
                if (!MasterDataPatchService.IsSameMasterVersion(patch.MasterVersion, current))
                {
                    MasterMemoryDebugLog.Warning(
                        $"Saved patch was created for master version {patch.MasterVersion} (current: {current}); auto load skipped. Use Load Patch to force load.");
                    return;
                }
                s_pending = patch;
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Error("Failed to read saved patch: " + e.Message);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_loaded = false;
            s_pending = null;
            s_appliedTables.Clear();
        }
    }
}
