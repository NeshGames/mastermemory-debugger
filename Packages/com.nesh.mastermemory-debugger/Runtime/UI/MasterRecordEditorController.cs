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
        readonly Button copyJsonButton;
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
            Button copyJsonButton,
            Action<string, bool> setStatus)
        {
            this.titleLabel = titleLabel;
            this.stateLabel = stateLabel;
            this.container = container;
            this.applyButton = applyButton;
            this.revertButton = revertButton;
            this.resetButton = resetButton;
            this.copyJsonButton = copyJsonButton;
            this.setStatus = setStatus;

            applyButton.clicked += OnApplyClicked;
            revertButton.clicked += Revert;
            resetButton.clicked += ResetRecord;
            copyJsonButton.clicked += CopyJson;
            Show(null);
        }

        public MasterMemoryRecordDescriptor Record => record;

        /// <summary>A reference button was clicked: the reference and the value currently in the editor.</summary>
        public event Action<MasterMemoryReference, object> ReferenceRequested;

        public bool IsDirty => isDirty;

        /// <summary>The panel that contains the field editors (used to route Enter to Apply).</summary>
        public VisualElement Container => container;

        public void Dispose()
        {
            applyButton.clicked -= OnApplyClicked;
            revertButton.clicked -= Revert;
            resetButton.clicked -= ResetRecord;
            copyJsonButton.clicked -= CopyJson;
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

        /// <summary>Drops unapplied edits (the user chose Discard).</summary>
        public void DiscardEdits()
        {
            if (!isDirty) return;
            Rebuild();
            setStatus("Unapplied edits discarded.", false);
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
            var spacer = new VisualElement();
            spacer.AddToClassList("mm-debugger__spacer");
            nameContainer.Add(spacer);

            // name line: name, badges, original value, reference jump; the editor takes the full width below
            row.OriginalLabel = new Label();
            row.OriginalLabel.AddToClassList("mm-debugger__field-original");
            nameContainer.Add(row.OriginalLabel);

            var reference = MasterMemoryReferences.Find(record.Table, field.Name);
            if (reference != null && MasterMemoryDebugRegistry.TryGetTable(reference.TargetType, out var target))
            {
                var jump = new Button(() => ReferenceRequested?.Invoke(reference, field.GetValue(workingCopy)))
                {
                    text = "→ " + target.TableName,
                    tooltip = reference + "  (MasterMemory Validate)",
                };
                jump.AddToClassList("mm-debugger__button");
                jump.AddToClassList("mm-debugger__reference-button");
                nameContainer.Add(jump);
            }
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
            copyJsonButton.SetEnabled(hasRecord);
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

        void OnApplyClicked() => TryApply();

        /// <summary>Applies the edits. Returns false when nothing was applied because of an error.</summary>
        public bool TryApply()
        {
            if (record == null) return false;
            if (!isDirty) return true;
            var table = record.Table;

            object key;
            try
            {
                key = table.GetPrimaryKey(workingCopy);
            }
            catch (Exception e)
            {
                setStatus("Apply failed: " + e.Message, true);
                return false;
            }
            if (!Equals(key, record.PrimaryKey))
            {
                setStatus("Apply failed: the primary key can not be changed.", true);
                return false;
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
            return true;
        }

        bool DiffersFromOriginal(object candidate)
        {
            foreach (var field in record.Table.TypeDescriptor.Fields)
            {
                if (!MasterDataValueUtility.AreEqual(field.GetValue(record.Original), field.GetValue(candidate))) return true;
            }
            return false;
        }

        /// <summary>Copies the record as shown (including unapplied edits) as JSON.</summary>
        void CopyJson()
        {
            if (record == null) return;
            string json;
            try
            {
                json = MasterDataRecordJson.ToJson(workingCopy ?? record.Current);
            }
            catch (Exception e)
            {
                setStatus("Copy failed: " + e.Message, true);
                return;
            }
            var fileName = MasterDataPatchStorage.NormalizeName($"{record.Table.TableName}_{record.KeyText}") + ".json";
            var result = MasterDataPatchExporter.CopyToClipboard(json, fileName);
            setStatus($"{record.Table.TableName} {record.KeyText}: {result.Message}", !result.Succeeded);
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
