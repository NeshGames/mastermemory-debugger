using System.Collections.Generic;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Writes override changes made from the debugger to the Console (colored in the Editor):
    /// <code>
    /// [MasterMemoryDebugger] Override applied: SkillMaster 1001 (Fireball)
    ///   Damage: 120 → 185
    /// </code>
    /// Controlled by <see cref="MasterMemoryDebuggerSettings.LogOverrideChanges"/>.
    /// </summary>
    internal static class MasterMemoryChangeLog
    {
        const string Prefix = "[MasterMemoryDebugger] ";

        static bool Enabled => MasterMemoryDebuggerSettings.Current.LogOverrideChanges;

        public static void Applied(MasterMemoryTableDescriptor table, object key, object before, object after)
        {
            if (!Enabled) return;
            Write($"Override applied: {Subject(table, key, after)}", MasterDataDiffUtility.GetChanges(before, after));
        }

        public static void Removed(MasterMemoryTableDescriptor table, object key, object before, object original, string reason)
        {
            if (!Enabled) return;
            Write($"Override {reason}: {Subject(table, key, original)}", MasterDataDiffUtility.GetChanges(before, original));
        }

        public static void ResetAll(int count)
        {
            if (!Enabled) return;
            Debug.Log(MasterDataDiffUtility.UseRichText
                ? $"<color=#FFB84C><b>{Prefix}All overrides reset</b></color> ({count} records, every value is back to the MasterMemory original)"
                : $"{Prefix}All overrides reset ({count} records, every value is back to the MasterMemory original)");
        }

        /// <summary>Lists every override after a patch was loaded (original → patched values).</summary>
        public static void PatchLoaded(string patchName, MasterDataPatchApplyResult result)
        {
            if (!Enabled) return;
            var richText = MasterDataDiffUtility.UseRichText;
            var sb = new System.Text.StringBuilder();
            var title = $"{Prefix}Patch \"{patchName}\" loaded: {result.AppliedRecords} records, {result.AppliedFields} fields";
            sb.Append(richText ? $"<color=#FFB84C><b>{title}</b></color>" : title);

            foreach (var table in MasterMemoryDebugRegistry.Tables)
            {
                foreach (var entry in MasterMemoryDebugRuntime.Store.GetEntries(table.RecordType))
                {
                    if (!table.TryFindOriginal(entry.Key.PrimaryKey, out var original)) continue;
                    sb.Append('\n').Append(Subject(table, entry.Key.PrimaryKey, entry.Value));
                    foreach (var change in MasterDataDiffUtility.GetChanges(original, entry.Value))
                    {
                        MasterDataDiffUtility.AppendChange(sb, change, richText);
                    }
                }
            }
            Debug.Log(sb.ToString());
        }

        static void Write(string title, List<MasterDataFieldChange> changes)
        {
            Debug.Log(MasterDataDiffUtility.Format(Prefix + title, changes, MasterDataDiffUtility.UseRichText));
        }

        static string Subject(MasterMemoryTableDescriptor table, object key, object record)
        {
            var subject = table.TableName + " " + MasterDataValueUtility.FormatKey(key);
            var name = table.GetDisplayName(record);
            return string.IsNullOrEmpty(name) ? subject : $"{subject} ({name})";
        }
    }
}
