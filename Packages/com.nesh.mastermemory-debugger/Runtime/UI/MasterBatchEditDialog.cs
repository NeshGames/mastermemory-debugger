using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// "Batch Edit" dialog: one field of every record that matches the search is set, increased or multiplied
    /// (<see cref="MasterMemoryBatchEdit"/>). One undo step.
    /// </summary>
    internal static class MasterBatchEditDialog
    {
        static readonly string[] s_operationNames = { "Set  =", "Add  +", "Multiply  ×" };

        // the last choices, offered again
        static string s_lastField;
        static MasterMemoryBatchOperation s_lastOperation = MasterMemoryBatchOperation.Multiply;
        static string s_lastValue = "1.1";

        public static void Show(MasterMemoryDebuggerDialog dialog, MasterMemoryTableDescriptor table, List<MasterMemoryRecordDescriptor> records, Action<string, bool> setStatus)
        {
            var fields = new List<MasterMemoryFieldDescriptor>();
            foreach (var field in table.TypeDescriptor.Fields)
            {
                if (MasterMemoryBatchEdit.CanEdit(field)) fields.Add(field);
            }
            if (fields.Count == 0)
            {
                setStatus($"{table.TableName} has no field that can be batch edited (keys, lists and complex members can not).", true);
                return;
            }
            if (records.Count == 0)
            {
                setStatus("No record matches the search.", true);
                return;
            }

            var labels = new List<string>();
            foreach (var field in fields) labels.Add(Label(table, field));
            var selectedIndex = Math.Max(0, fields.FindIndex(x => x.Name == s_lastField));

            var content = new VisualElement();
            var fieldChoice = new DropdownField("Field", labels, selectedIndex);
            var operationChoice = new DropdownField("Operation", new List<string>(s_operationNames), 0);
            // enums and booleans pick from their values, everything else is typed
            var valueField = new TextField("Value") { value = s_lastValue };
            var valueChoice = new DropdownField("Value", new List<string> { string.Empty }, 0);
            var hint = new Label();
            hint.AddToClassList("mm-debugger__hint");
            content.Add(fieldChoice);
            content.Add(operationChoice);
            content.Add(valueField);
            content.Add(valueChoice);
            content.Add(hint);

            MasterMemoryFieldDescriptor Selected() => fields[Math.Max(0, fieldChoice.index)];
            bool IsChoice() => valueChoice.style.display.value != DisplayStyle.None;
            string ValueText() => IsChoice() ? valueChoice.value : valueField.value;

            void RefreshOperations()
            {
                var field = Selected();
                var isNumber = MasterMemoryBatchEdit.IsNumber(field);
                operationChoice.choices = isNumber ? new List<string>(s_operationNames) : new List<string> { s_operationNames[0] };
                var operation = isNumber ? s_lastOperation : MasterMemoryBatchOperation.Set;
                operationChoice.index = (int)operation;

                var choices = ValueChoices(field);
                valueField.style.display = choices == null ? DisplayStyle.Flex : DisplayStyle.None;
                valueChoice.style.display = choices == null ? DisplayStyle.None : DisplayStyle.Flex;
                if (choices != null)
                {
                    // start from the value of the first record
                    var current = field.GetValue(records[0].Current);
                    var text = current == null ? Null : MasterDataValueUtility.Format(current);
                    valueChoice.choices = choices;
                    valueChoice.index = Math.Max(0, choices.FindIndex(x => string.Equals(x, text, StringComparison.OrdinalIgnoreCase)));
                }
                hint.text = HintText(field);
            }

            fieldChoice.RegisterValueChangedCallback(_ => RefreshOperations());
            RefreshOperations();

            dialog.Show(
                "Batch Edit",
                $"Changes {records.Count} records of {MasterMemoryDebugLocalization.GetTableLabel(table)} that match the search (all matches, not only the rows shown). Undo reverts it.",
                content,
                new MasterMemoryDebuggerDialog.DialogButton("Cancel", null),
                new MasterMemoryDebuggerDialog.DialogButton("Apply", () =>
                {
                    var field = Selected();
                    var operation = (MasterMemoryBatchOperation)Math.Max(0, operationChoice.index);
                    s_lastField = field.Name;
                    if (MasterMemoryBatchEdit.IsNumber(field)) s_lastOperation = operation;
                    if (!IsChoice()) s_lastValue = valueField.value;
                    Apply(table, records, field, operation, ValueText(), setStatus);
                }, isPrimary: true));
            if (!IsChoice()) valueField.schedule.Execute(() => valueField.Focus());
        }

        static void Apply(MasterMemoryTableDescriptor table, List<MasterMemoryRecordDescriptor> records, MasterMemoryFieldDescriptor field,
            MasterMemoryBatchOperation operation, string value, Action<string, bool> setStatus)
        {
            var description = MasterMemoryBatchEdit.Describe(field, operation, value);
            MasterMemoryBatchEditResult result;
            using (MasterMemoryDebugHistory.Record($"Batch edit {table.TableName} {description}"))
            {
                result = MasterMemoryBatchEdit.Apply(records, field, operation, value);
            }
            if (result.InvalidValue != null)
            {
                setStatus($"Batch edit not applied: {result.InvalidValue}", true);
                return;
            }

            MasterMemoryDebuggerController.LogWarnings(result.Errors);
            var message = $"Batch edit {table.TableName} {description}: {result.Changed} changed, {result.Unchanged} unchanged";
            if (result.Failed > 0) message += $", {result.Failed} failed (see Console)";
            if (result.Changed > 0) MasterMemoryDebugLog.Info(message);
            setStatus(message, result.Failed > 0);
        }

        const string Null = "null";

        /// <summary>The values of an enum or bool field (and null when nullable); null for the fields typed as text.</summary>
        internal static List<string> ValueChoices(MasterMemoryFieldDescriptor field)
        {
            List<string> choices;
            switch (field.Kind)
            {
                case MasterDataValueKind.Enum:
                    choices = new List<string>(Enum.GetNames(field.ValueType));
                    break;
                case MasterDataValueKind.Boolean:
                    choices = new List<string> { "true", "false" };
                    break;
                default:
                    return null;
            }
            if (field.IsNullable) choices.Add(Null);
            return choices;
        }

        static string Label(MasterMemoryTableDescriptor table, MasterMemoryFieldDescriptor field)
        {
            var label = MasterMemoryDebugLocalization.GetFieldLabel(table, field);
            return label == field.Name ? $"{field.Name}  ({field.ValueType.Name})" : $"{label}  ({field.Name}, {field.ValueType.Name})";
        }

        static string HintText(MasterMemoryFieldDescriptor field)
        {
            var nullable = field.IsNullable ? "  null clears the value." : string.Empty;
            switch (field.Kind)
            {
                case MasterDataValueKind.Boolean:
                case MasterDataValueKind.Enum:
                    return "Every record gets the selected value.";
                case MasterDataValueKind.FlagsEnum:
                    return "Enum names joined with |: " + string.Join(", ", Enum.GetNames(field.ValueType)) + "." + nullable;
                case MasterDataValueKind.String:
                    return "The text, as typed.";
                case MasterDataValueKind.Custom:
                    return $"Set: the new value, as shown ({field.ValueType.Name})." + nullable;
                default:
                    return "Set: the new value.  Add: a number to add (negative to subtract).  Multiply: 1.1 = +10%; integers are rounded." + nullable;
            }
        }
    }
}
