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
    /// Console output is controlled by <see cref="MasterMemoryDebuggerSettings.LogOverrideChanges"/>;
    /// the in-game log panel (<see cref="MasterMemoryDebuggerMessages"/>) always receives the plain text.
    /// </summary>
    internal static class MasterMemoryChangeLog
    {
        const string Prefix = "[MasterMemoryDebugger] ";

        static bool ConsoleEnabled => MasterMemoryDebuggerSettings.Current.LogOverrideChanges;

        public static void Applied(MasterMemoryTableDescriptor table, object key, object before, object after)
        {
            Write($"Override applied: {Subject(table, key, after)}", MasterDataDiffUtility.GetChanges(before, after));
        }

        public static void Removed(MasterMemoryTableDescriptor table, object key, object before, object original, string reason)
        {
            Write($"Override {reason}: {Subject(table, key, original)}", MasterDataDiffUtility.GetChanges(before, original));
        }

        public static void ResetAll(int count)
        {
            MasterMemoryDebuggerMessages.Add(MasterMemoryDebuggerMessageType.Change, $"All overrides reset ({count} records)");
            if (!ConsoleEnabled) return;
            Debug.Log(MasterDataDiffUtility.UseRichText
                ? $"<color=#FFB84C><b>{Prefix}All overrides reset</b></color> ({count} records, every value is back to the MasterMemory original)"
                : $"{Prefix}All overrides reset ({count} records, every value is back to the MasterMemory original)");
        }

        /// <summary>Lists every override after a patch was loaded (original → patched values).</summary>
        public static void PatchLoaded(string patchName, MasterDataPatchApplyResult result)
        {
            var title = $"Patch \"{patchName}\" loaded: {result.AppliedRecords} records, {result.AppliedFields} fields";
            var plain = new System.Text.StringBuilder(title);
            var rich = new System.Text.StringBuilder($"<color=#FFB84C><b>{Prefix}{title}</b></color>");
            foreach (var entry in MasterMemoryChangeSummary.Build())
            {
                if (entry.Status != MasterMemoryChangeStatus.Changed) continue;
                var subject = Subject(entry.Table, entry.PrimaryKey, entry.Current);
                plain.Append('\n').Append(subject);
                rich.Append('\n').Append(subject);
                foreach (var change in entry.Changes)
                {
                    MasterDataDiffUtility.AppendChange(plain, change, false);
                    MasterDataDiffUtility.AppendChange(rich, change, true);
                }
            }
            MasterMemoryDebuggerMessages.Add(MasterMemoryDebuggerMessageType.Change, plain.ToString());
            if (ConsoleEnabled) Debug.Log(MasterDataDiffUtility.UseRichText ? rich.ToString() : Prefix + plain);
        }

        static void Write(string title, List<MasterDataFieldChange> changes)
        {
            MasterMemoryDebuggerMessages.Add(MasterMemoryDebuggerMessageType.Change, MasterDataDiffUtility.Format(title, changes, false));
            if (ConsoleEnabled) Debug.Log(MasterDataDiffUtility.Format(Prefix + title, changes, MasterDataDiffUtility.UseRichText));
        }

        static string Subject(MasterMemoryTableDescriptor table, object key, object record)
        {
            var subject = table.TableName + " " + MasterDataValueUtility.FormatKey(key);
            var name = table.GetDisplayName(record);
            return string.IsNullOrEmpty(name) ? subject : $"{subject} ({name})";
        }
    }
}
