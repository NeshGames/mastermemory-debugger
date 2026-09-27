using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// "New record" / "Duplicate" dialog: asks for the primary key of a record added as an override
    /// (<see cref="MasterMemoryRecordFactory"/>). One undo step.
    /// </summary>
    internal static class MasterRecordCreateDialog
    {
        /// <param name="source">The record to copy, or null for a record with default values.</param>
        /// <param name="created">Receives the key of the added record.</param>
        public static void Show(MasterMemoryDebuggerDialog dialog, MasterMemoryTableDescriptor table, MasterMemoryRecordDescriptor source,
            Action<string, bool> setStatus, Action<object> created, List<string> keyTexts = null, string error = null)
        {
            if (!MasterMemoryRecordFactory.CanAdd(table, out var reason))
            {
                setStatus(reason, true);
                return;
            }
            var template = source?.Current;
            keyTexts ??= MasterMemoryRecordFactory.SuggestKey(table, template);

            var content = new VisualElement();
            var keys = table.TypeDescriptor.PrimaryKeyFields;
            var fields = new List<TextField>();
            for (var i = 0; i < keys.Count; i++)
            {
                var field = new TextField(MasterMemoryDebugLocalization.GetFieldLabel(table, keys[i]) + " (PK)") { value = keyTexts[i] };
                field.tooltip = keys[i].ToString();
                fields.Add(field);
                content.Add(field);
            }
            var hint = new Label(error ?? "The record exists only as an override until the master data gets it: a rebuilt database " +
                                 "(AutoRebuild) and TryGetOverride see it. Undo or Delete removes it.");
            hint.AddToClassList("mm-debugger__hint");
            hint.EnableInClassList("mm-debugger__hint--error", error != null);
            content.Add(hint);

            var title = source != null ? $"Duplicate {table.TableName} {source.KeyText}" : $"New {table.TableName} record";
            dialog.Show(
                title,
                source != null ? "A copy of the record with a new primary key." : "A record with default values and this primary key.",
                content,
                new MasterMemoryDebuggerDialog.DialogButton("Cancel", null),
                new MasterMemoryDebuggerDialog.DialogButton(source != null ? "Duplicate" : "Create", () =>
                {
                    var texts = fields.ConvertAll(x => x.value);
                    if (!MasterMemoryRecordFactory.TryCreate(table, template, texts, out var record, out var key, out var createError))
                    {
                        // shown again with the typed values and the reason
                        Show(dialog, table, source, setStatus, created, texts, createError);
                        return;
                    }
                    using (MasterMemoryDebugHistory.Record($"{(source != null ? "Duplicate" : "New")} {table.TableName} {MasterDataValueUtility.FormatKey(key)}"))
                    {
                        MasterMemoryDebugRuntime.Store.Set(table.RecordType, key, record);
                    }
                    MasterMemoryChangeLog.RecordChanged(table, key, record, source != null ? $"added (copy of {source.KeyText})" : "added");
                    setStatus($"Record added: {table.TableName} {MasterDataValueUtility.FormatKey(key)}", false);
                    created?.Invoke(key);
                }, isPrimary: true));
            if (fields.Count > 0) fields[fields.Count - 1].schedule.Execute(() => fields[fields.Count - 1].Focus());
        }
    }
}
