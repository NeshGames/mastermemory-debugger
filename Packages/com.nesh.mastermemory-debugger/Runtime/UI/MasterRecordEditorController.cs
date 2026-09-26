using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Record inspector. Edits happen on a clone ("working copy"); Apply Override stores the clone.
    /// Primary / secondary keys and complex members are always read-only.
    /// </summary>
    internal sealed class MasterRecordEditorController : IDisposable
    {
        sealed class FieldRow
        {
            public MasterMemoryFieldDescriptor Field;
            public VisualElement Root;
            public Label OriginalLabel;
        }

        readonly Label titleLabel;
        readonly Label stateLabel;
        readonly ScrollView container;
        readonly Button applyButton;
        readonly Button revertButton;
        readonly Button resetButton;
        readonly Action<string, bool> setStatus;
        readonly List<FieldRow> rows = new List<FieldRow>();

        MasterMemoryRecordDescriptor record;
        object workingCopy;
        bool isDirty;
        bool isWritingStore;

        public MasterRecordEditorController(
            Label titleLabel,
            Label stateLabel,
            ScrollView container,
            Button applyButton,
            Button revertButton,
            Button resetButton,
            Action<string, bool> setStatus)
        {
            this.titleLabel = titleLabel;
            this.stateLabel = stateLabel;
            this.container = container;
            this.applyButton = applyButton;
            this.revertButton = revertButton;
            this.resetButton = resetButton;
            this.setStatus = setStatus;

            applyButton.clicked += Apply;
            revertButton.clicked += Revert;
            resetButton.clicked += ResetRecord;
            Show(null);
        }

        public MasterMemoryRecordDescriptor Record => record;

        public bool IsDirty => isDirty;

        public void Dispose()
        {
            applyButton.clicked -= Apply;
            revertButton.clicked -= Revert;
            resetButton.clicked -= ResetRecord;
        }

        public void Show(MasterMemoryRecordDescriptor newRecord)
        {
            if (isDirty && newRecord != record && record != null)
            {
                setStatus($"Unapplied edits of {record.Table.TableName} {record.KeyText} were discarded.", false);
            }
            record = newRecord;
            Rebuild();
        }

        /// <summary>Overrides changed elsewhere (reset all, patch load, code): reload from the store.</summary>
        public void OnOverridesChanged()
        {
            if (record == null || isWritingStore) return;
            if (isDirty) setStatus("Overrides changed; unapplied edits were discarded.", false);
            Rebuild();
        }

        void Rebuild()
        {
            container.Clear();
            rows.Clear();
            isDirty = false;

            if (record == null)
            {
                workingCopy = null;
                titleLabel.text = "Record Inspector";
                var hint = new Label("Select a record.");
                hint.AddToClassList("mm-debugger__hint");
                container.Add(hint);
                UpdateState();
                return;
            }

            var settings = MasterMemoryDebuggerSettings.Current;
            var editable = settings.AllowEditing;
            workingCopy = editable ? MasterDataCloneUtility.Clone(record.Current) : record.Current;
            titleLabel.text = $"{record.Table.TableName}   {record.KeyText}";

            foreach (var field in record.Table.TypeDescriptor.Fields)
            {
                if (field.IsSecondaryKey && !field.IsPrimaryKey && !settings.ShowSecondaryKeys) continue;
                var row = CreateRow(field, editable);
                rows.Add(row);
                container.Add(row.Root);
            }
            RefreshOriginalMarkers();
            UpdateState();
        }

        FieldRow CreateRow(MasterMemoryFieldDescriptor field, bool editable)
        {
            var row = new FieldRow { Field = field, Root = new VisualElement() };
            row.Root.AddToClassList("mm-debugger__field");

            var nameContainer = new VisualElement();
            nameContainer.AddToClassList("mm-debugger__field-name-container");
            var nameLabel = new Label(field.Name) { tooltip = field.FieldType.FullName };
            nameLabel.AddToClassList("mm-debugger__field-name");
            nameContainer.Add(nameLabel);
            if (field.IsPrimaryKey) nameContainer.Add(CreateBadge("PK", "mm-debugger__badge--pk", "Primary key (read-only)"));
            if (field.IsSecondaryKey) nameContainer.Add(CreateBadge("SK", "mm-debugger__badge--sk", "Secondary key (read-only: MasterMemory indexes are not updated by overrides)"));
            if (!field.IsKey && !field.CanEdit) nameContainer.Add(CreateBadge("RO", "mm-debugger__badge--ro", "Read-only type"));
            row.Root.Add(nameContainer);

            var value = field.GetValue(workingCopy);
            VisualElement editor;
            if (editable && field.CanEdit)
            {
                editor = MasterFieldDrawerFactory.CreateEditor(field, value, newValue => OnFieldChanged(row, newValue));
            }
            else
            {
                editor = MasterFieldDrawerFactory.CreateReadOnly(value);
                if (field.IsKey) editor.AddToClassList("mm-debugger__field-readonly--key");
            }
            editor.AddToClassList("mm-debugger__field-value");
            row.Root.Add(editor);

            row.OriginalLabel = new Label();
            row.OriginalLabel.AddToClassList("mm-debugger__field-original");
            row.Root.Add(row.OriginalLabel);
            return row;
        }

        static Label CreateBadge(string text, string modifierClass, string tooltip)
        {
            var badge = new Label(text) { tooltip = tooltip };
            badge.AddToClassList("mm-debugger__badge");
            badge.AddToClassList(modifierClass);
            return badge;
        }

        void OnFieldChanged(FieldRow row, object newValue)
        {
            try
            {
                row.Field.SetValue(workingCopy, newValue);
            }
            catch (Exception e)
            {
                setStatus($"Failed to set {row.Field.Name}: {e.Message}", true);
                return;
            }
            isDirty = true;
            RefreshOriginalMarker(row);
            UpdateState();
        }

        void RefreshOriginalMarkers()
        {
            foreach (var row in rows) RefreshOriginalMarker(row);
        }

        void RefreshOriginalMarker(FieldRow row)
        {
            var field = row.Field;
            var originalValue = field.GetValue(record.Original);
            var changed = !field.IsKey && !MasterDataValueUtility.AreEqual(originalValue, field.GetValue(workingCopy));
            row.Root.EnableInClassList("mm-debugger__field--modified", changed);
            row.OriginalLabel.text = changed ? "Original: " + MasterDataValueUtility.Format(originalValue) : string.Empty;
        }

        void UpdateState()
        {
            var settings = MasterMemoryDebuggerSettings.Current;
            var hasRecord = record != null;
            var isOverridden = hasRecord && record.IsModified;

            applyButton.SetEnabled(hasRecord && settings.AllowEditing && isDirty);
            revertButton.SetEnabled(hasRecord && isDirty);
            resetButton.SetEnabled(isOverridden);
            applyButton.style.display = settings.AllowEditing ? DisplayStyle.Flex : DisplayStyle.None;
            revertButton.style.display = settings.AllowEditing ? DisplayStyle.Flex : DisplayStyle.None;

            var state = !hasRecord ? string.Empty
                : isDirty ? "Unapplied edits"
                : isOverridden ? "Overridden"
                : "Original";
            stateLabel.text = state;
            stateLabel.EnableInClassList("mm-debugger__record-state--dirty", isDirty);
            stateLabel.EnableInClassList("mm-debugger__record-state--modified", !isDirty && isOverridden);
        }

        void Apply()
        {
            if (record == null || !isDirty) return;
            var table = record.Table;

            object key;
            try
            {
                key = table.GetPrimaryKey(workingCopy);
            }
            catch (Exception e)
            {
                setStatus("Apply failed: " + e.Message, true);
                return;
            }
            if (!Equals(key, record.PrimaryKey))
            {
                setStatus("Apply failed: the primary key can not be changed.", true);
                return;
            }

            var store = MasterMemoryDebugRuntime.Store;
            var before = record.Current;
            isWritingStore = true;
            try
            {
                if (DiffersFromOriginal(workingCopy))
                {
                    store.Set(table.RecordType, record.PrimaryKey, workingCopy);
                    MasterMemoryChangeLog.Applied(table, record.PrimaryKey, before, workingCopy);
                    setStatus($"Override applied: {table.TableName} {record.KeyText}", false);
                }
                else
                {
                    // every value equals the original: an override would be a no-op
                    store.Remove(table.RecordType, record.PrimaryKey);
                    MasterMemoryChangeLog.Removed(table, record.PrimaryKey, before, record.Original, "removed (values equal the original)");
                    setStatus($"Values equal the original; override removed: {table.TableName} {record.KeyText}", false);
                }
            }
            finally
            {
                isWritingStore = false;
            }
            Rebuild();
        }

        bool DiffersFromOriginal(object candidate)
        {
            foreach (var field in record.Table.TypeDescriptor.Fields)
            {
                if (!MasterDataValueUtility.AreEqual(field.GetValue(record.Original), field.GetValue(candidate))) return true;
            }
            return false;
        }

        void Revert()
        {
            if (record == null) return;
            Rebuild();
            setStatus("Edits reverted.", false);
        }

        void ResetRecord()
        {
            if (record == null) return;
            var before = record.Current;
            isWritingStore = true;
            try
            {
                if (MasterMemoryDebugRuntime.Store.Remove(record.Table.RecordType, record.PrimaryKey))
                {
                    MasterMemoryChangeLog.Removed(record.Table, record.PrimaryKey, before, record.Original, "reset");
                    setStatus($"Override reset: {record.Table.TableName} {record.KeyText}", false);
                }
            }
            finally
            {
                isWritingStore = false;
            }
            Rebuild();
        }
    }
}
