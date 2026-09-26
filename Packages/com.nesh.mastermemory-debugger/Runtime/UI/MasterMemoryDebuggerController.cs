using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Root controller. Owns every event subscription of the debugger UI (View + Controller);
    /// the UXML only describes layout.
    /// </summary>
    internal sealed class MasterMemoryDebuggerController : IDisposable
    {
        /// <summary>Named elements of MasterMemoryDebugger.uxml used by the controllers.</summary>
        internal static readonly string[] RequiredElementNames =
        {
            "mm-window", "mm-status", "mm-master-version", "mm-override-count", "mm-dialog-layer",
            "mm-table-list", "mm-search", "mm-modified-only", "mm-record-list", "mm-record-count",
            "mm-inspector-title", "mm-record-state", "mm-inspector", "mm-apply", "mm-revert", "mm-reset-record", "mm-copy-json",
            "mm-close", "mm-patch-list", "mm-patch-name", "mm-save-patch", "mm-load-patch", "mm-delete-patch", "mm-import-patch",
            "mm-export-patch", "mm-open-folder", "mm-reset-all",
            "mm-scale-down", "mm-scale-up", "mm-main", "mm-changes", "mm-changes-panel", "mm-changes-list", "mm-changes-summary",
            "mm-log", "mm-log-toggle",
        };

        const float MinScale = 0.5f;
        const float MaxScale = 2f;
        const float ScaleStep = 0.1f;

        // Kept across open / close so the debugger reopens where it was.
        static class Session
        {
            public static string TableName;
            public static object RecordKey;
            public static string Query;
            public static bool ModifiedOnly;
            public static float Scale = 1f;
            public static string PatchName;
        }

        readonly MasterMemoryDebuggerDocument host;
        readonly VisualElement root;
        readonly VisualElement mainPanel;
        readonly Button changesButton;
        readonly MasterChangesController changes;
        readonly ScrollView logView;
        readonly Button logToggle;
        MasterMemoryTableDescriptor shownTable;
        readonly MasterTableListController tableList;
        readonly MasterRecordListController recordList;
        readonly MasterRecordEditorController editor;
        readonly MasterMemoryDebuggerDialog dialog;
        readonly Label statusLabel;
        readonly Label versionLabel;
        readonly Label overrideCountLabel;
        readonly DropdownField patchList;
        readonly TextField patchName;
        readonly Button loadPatchButton;
        readonly Button deletePatchButton;
        readonly List<(Button button, Action action)> buttons = new List<(Button, Action)>();

        public MasterMemoryDebuggerController(VisualElement root, MasterMemoryDebuggerDocument host)
        {
            this.host = host;
            this.root = root;
            var settings = MasterMemoryDebuggerSettings.Current;

            statusLabel = Required<Label>(root, "mm-status");
            versionLabel = Required<Label>(root, "mm-master-version");
            overrideCountLabel = Required<Label>(root, "mm-override-count");
            dialog = new MasterMemoryDebuggerDialog(Required<VisualElement>(root, "mm-dialog-layer"));

            patchList = Required<DropdownField>(root, "mm-patch-list");
            patchName = Required<TextField>(root, "mm-patch-name");
            loadPatchButton = Required<Button>(root, "mm-load-patch");
            deletePatchButton = Required<Button>(root, "mm-delete-patch");
            patchName.textEdition.placeholder = "patch name";
            patchName.SetValueWithoutNotify(Session.PatchName ?? MasterDataPatchStorage.DefaultPatchName);
            patchList.RegisterValueChangedCallback(OnPatchSelected);

            tableList = new MasterTableListController(Required<TreeView>(root, "mm-table-list"));
            recordList = new MasterRecordListController(
                Required<TextField>(root, "mm-search"),
                Required<Toggle>(root, "mm-modified-only"),
                Required<MultiColumnListView>(root, "mm-record-list"),
                Required<Label>(root, "mm-record-count"));
            editor = new MasterRecordEditorController(
                Required<Label>(root, "mm-inspector-title"),
                Required<Label>(root, "mm-record-state"),
                Required<ScrollView>(root, "mm-inspector"),
                Required<Button>(root, "mm-apply"),
                Required<Button>(root, "mm-revert"),
                Required<Button>(root, "mm-reset-record"),
                Required<Button>(root, "mm-copy-json"),
                SetStatus);

            mainPanel = Required<VisualElement>(root, "mm-main");
            changesButton = Required<Button>(root, "mm-changes");
            changes = new MasterChangesController(
                Required<VisualElement>(root, "mm-changes-panel"),
                Required<ScrollView>(root, "mm-changes-list"),
                Required<Label>(root, "mm-changes-summary"),
                OpenRecord,
                SetStatus);
            logView = Required<ScrollView>(root, "mm-log");
            logToggle = Required<Button>(root, "mm-log-toggle");
            logView.style.display = DisplayStyle.None;

            Bind(root, "mm-close", RuntimeMasterMemoryDebugger.Close);
            Bind(root, "mm-save-patch", SavePatch);
            Bind(root, "mm-load-patch", LoadPatch);
            Bind(root, "mm-delete-patch", ConfirmDeletePatch);
            Bind(root, "mm-import-patch", ImportPatch);
            Bind(root, "mm-changes", ToggleChanges);
            Bind(root, "mm-log-toggle", ToggleLog);
            Bind(root, "mm-export-patch", ExportPatch);
            Bind(root, "mm-open-folder", MasterDataPatchExporter.RevealDataDirectory);
            Bind(root, "mm-reset-all", ConfirmResetAll);
            Bind(root, "mm-scale-down", () => ChangeScale(-ScaleStep));
            Bind(root, "mm-scale-up", () => ChangeScale(ScaleStep));

            SetVisible(root, "mm-save-patch", settings.AllowPatchSave);
            SetVisible(root, "mm-patch-name", settings.AllowPatchSave);
            SetVisible(root, "mm-delete-patch", settings.AllowPatchSave);
            SetVisible(root, "mm-export-patch", settings.AllowPatchSave);
            SetVisible(root, "mm-open-folder", MasterDataPatchExporter.CanRevealExports);
            SetVisible(root, "mm-reset-all", settings.AllowEditing);
            var canScale = host.OwnedPanelSettings != null;
            SetVisible(root, "mm-scale-down", canScale);
            SetVisible(root, "mm-scale-up", canScale);
            if (canScale) host.OwnedPanelSettings.scale = Session.Scale;

            tableList.TableSelected += OnTableSelected;
            recordList.RecordSelected += OnRecordSelected;
            MasterMemoryDebugRegistry.TablesChanged += OnTablesChanged;
            MasterMemoryDebugRuntime.OverridesChanged += OnOverridesChanged;
            MasterMemoryDebuggerMessages.Changed += OnMessagesChanged;
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            // restore the previous session
            recordList.SetState(Session.Query, Session.ModifiedOnly);
            var pendingKey = Session.RecordKey;
            tableList.Reload(Session.TableName);
            if (pendingKey != null && tableList.SelectedTable != null) recordList.SelectByKey(pendingKey);

            RefreshHeader();
            RefreshPatchList(patchName.value);
            OnMessagesChanged();
            if (MasterMemoryDebugRegistry.Tables.Count == 0)
            {
                SetStatus("No table registered. Call MasterMemoryDebugRegistry.RegisterDatabase / RegisterTable.", true);
            }
            else
            {
                statusLabel.text = "Ready.  Enter: Apply   Esc: Close";
            }
        }

        public void Dispose()
        {
            Session.Query = recordList.Query;
            Session.ModifiedOnly = recordList.ModifiedOnly;
            Session.TableName = tableList.SelectedTable?.TableName;
            Session.RecordKey = editor.Record?.PrimaryKey;
            Session.PatchName = patchName.value;
            patchList.UnregisterValueChangedCallback(OnPatchSelected);

            MasterMemoryDebugRegistry.TablesChanged -= OnTablesChanged;
            MasterMemoryDebugRuntime.OverridesChanged -= OnOverridesChanged;
            MasterMemoryDebuggerMessages.Changed -= OnMessagesChanged;
            root.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            tableList.TableSelected -= OnTableSelected;
            recordList.RecordSelected -= OnRecordSelected;
            foreach (var (button, action) in buttons) button.clicked -= action;
            buttons.Clear();

            tableList.Dispose();
            recordList.Dispose();
            editor.Dispose();
        }

        // ------------------------------------------------------------------ events

        void OnTablesChanged()
        {
            tableList.Reload();
            RefreshHeader();
        }

        void OnTableSelected(MasterMemoryTableDescriptor table)
        {
            var previous = shownTable;
            RunAfterEditGuard(() => ShowTable(table), () => tableList.RestoreSelection(previous));
        }

        void ShowTable(MasterMemoryTableDescriptor table)
        {
            shownTable = table;
            editor.Show(null);
            recordList.SetTable(table);
        }

        void OnRecordSelected(MasterMemoryRecordDescriptor record)
        {
            var previous = editor.Record;
            RunAfterEditGuard(() => editor.Show(record), () => recordList.RestoreSelection(previous));
        }

        /// <summary>Asks Apply / Discard / Cancel when the inspector has unapplied edits, otherwise proceeds.</summary>
        void RunAfterEditGuard(Action proceed, Action cancel)
        {
            if (!editor.IsDirty || editor.Record == null)
            {
                proceed();
                return;
            }
            var record = editor.Record;
            dialog.Show(
                "Unapplied edits",
                $"{record.Table.TableName} {record.KeyText} has edits that are not applied yet.",
                new MasterMemoryDebuggerDialog.DialogButton("Cancel", cancel),
                new MasterMemoryDebuggerDialog.DialogButton("Discard", () =>
                {
                    editor.DiscardEdits();
                    proceed();
                }, isDanger: true),
                new MasterMemoryDebuggerDialog.DialogButton("Apply", () =>
                {
                    if (editor.TryApply()) proceed();
                    else cancel();
                }, isPrimary: true));
        }

        void OnOverridesChanged()
        {
            tableList.RefreshCounts();
            recordList.OnOverridesChanged();
            editor.OnOverridesChanged();
            changes.Refresh();
            RefreshHeader();
        }

        // ------------------------------------------------------------------ changes view

        void ToggleChanges()
        {
            if (changes.IsVisible) HideChanges();
            else ShowChanges();
        }

        void ShowChanges()
        {
            mainPanel.style.display = DisplayStyle.None;
            changes.Show();
            RefreshHeader();
        }

        void HideChanges()
        {
            changes.Hide();
            mainPanel.style.display = DisplayStyle.Flex;
            RefreshHeader();
        }

        /// <summary>Jumps from the Changes view to a record.</summary>
        void OpenRecord(MasterMemoryTableDescriptor table, object key)
        {
            RunAfterEditGuard(() =>
            {
                HideChanges();
                if (shownTable != table)
                {
                    tableList.RestoreSelection(table);
                    ShowTable(table);
                }
                if (!recordList.SelectByKey(key))
                {
                    // hidden by the search / Modified Only filter
                    recordList.SetState(string.Empty, false);
                    recordList.SetTable(table, key);
                }
            }, () => { });
        }

        // ------------------------------------------------------------------ log panel

        void ToggleLog()
        {
            var visible = logView.style.display.value == DisplayStyle.Flex;
            logView.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            if (!visible) RebuildLog();
        }

        void OnMessagesChanged()
        {
            logToggle.text = $"Log ({MasterMemoryDebuggerMessages.Messages.Count})";
            if (logView.style.display.value == DisplayStyle.Flex) RebuildLog();
        }

        void RebuildLog()
        {
            logView.Clear();
            Label last = null;
            foreach (var message in MasterMemoryDebuggerMessages.Messages)
            {
                last = new Label($"{message.Time:HH:mm:ss}  {message.Text}");
                last.AddToClassList("mm-debugger__log-entry");
                last.AddToClassList("mm-debugger__log-entry--" + message.Type.ToString().ToLowerInvariant());
                last.selection.isSelectable = true;
                logView.Add(last);
            }
            if (last == null)
            {
                var empty = new Label("No messages.");
                empty.AddToClassList("mm-debugger__hint");
                logView.Add(empty);
            }
            else
            {
                var target = last;
                logView.schedule.Execute(() => logView.ScrollTo(target));
            }
        }

        // ------------------------------------------------------------------ keyboard

        void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                    if (dialog.IsVisible) dialog.Cancel();
                    else RuntimeMasterMemoryDebugger.Close();
                    evt.StopPropagation();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    var target = evt.target as VisualElement;
                    if (dialog.IsVisible)
                    {
                        // Enter inserts a new line in the paste box
                        if (target?.GetFirstAncestorOfType<TextField>()?.multiline == true || (target as TextField)?.multiline == true) break;
                        dialog.Confirm();
                        evt.StopPropagation();
                    }
                    else if (editor.IsDirty && target != null && editor.Container.Contains(target))
                    {
                        editor.TryApply();
                        evt.StopPropagation();
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ patch

        void OnPatchSelected(ChangeEvent<string> evt)
        {
            if (!string.IsNullOrEmpty(evt.newValue)) patchName.SetValueWithoutNotify(evt.newValue);
            UpdatePatchButtons();
        }

        /// <summary>Reloads the saved patch names and selects <paramref name="select"/> when it exists.</summary>
        void RefreshPatchList(string select)
        {
            var names = MasterDataPatchStorage.ListPatchNames();
            patchList.choices = names;
            var normalized = MasterDataPatchStorage.NormalizeName(select);
            string value = null;
            if (normalized != null && names.Contains(normalized)) value = normalized;
            else if (names.Contains(patchList.value)) value = patchList.value;
            else if (names.Count > 0) value = names.Contains(MasterDataPatchStorage.DefaultPatchName) ? MasterDataPatchStorage.DefaultPatchName : names[0];
            patchList.SetValueWithoutNotify(value ?? string.Empty);
            UpdatePatchButtons();
        }

        void UpdatePatchButtons()
        {
            var hasSelection = !string.IsNullOrEmpty(patchList.value);
            loadPatchButton.SetEnabled(hasSelection);
            deletePatchButton.SetEnabled(hasSelection);
            patchList.SetEnabled(patchList.choices.Count > 0);
        }

        void SavePatch()
        {
            var name = MasterDataPatchStorage.NormalizeName(patchName.value) ?? MasterDataPatchStorage.DefaultPatchName;
            if (MasterDataPatchStorage.Exists(name) && name != patchList.value)
            {
                dialog.Show(
                    "Overwrite patch",
                    $"A patch named \"{name}\" already exists. Overwrite it?",
                    new MasterMemoryDebuggerDialog.DialogButton("Cancel", null),
                    new MasterMemoryDebuggerDialog.DialogButton("Overwrite", () => SavePatch(name), isDanger: true));
                return;
            }
            SavePatch(name);
        }

        void SavePatch(string name)
        {
            try
            {
                var warnings = new List<string>();
                var patch = MasterDataPatchService.CreatePatch(warnings);
                var path = MasterDataPatchStorage.Save(patch, name);
                LogWarnings(warnings);
                patchName.SetValueWithoutNotify(name);
                RefreshPatchList(name);
                SetStatus($"Patch \"{name}\" saved: {patch.RecordCount} records → {path}{WarningSuffix(warnings)}", false);
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Error("Save patch failed: " + e);
                SetStatus("Save failed: " + e.Message, true);
            }
        }

        void LoadPatch()
        {
            var name = patchList.value;
            if (string.IsNullOrEmpty(name))
            {
                SetStatus("No saved patch selected.", true);
                return;
            }

            MasterDataPatch patch;
            try
            {
                patch = MasterDataPatchStorage.Load(name);
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Error("Load patch failed: " + e);
                SetStatus("Load failed: " + e.Message, true);
                return;
            }
            if (patch == null)
            {
                SetStatus("Patch not found: " + MasterDataPatchStorage.GetPatchPath(name), true);
                RefreshPatchList(null);
                return;
            }

            var result = MasterDataPatchService.Apply(patch);
            if (result.Status == MasterDataPatchApplyStatus.VersionMismatch)
            {
                dialog.Show(
                    "Master version mismatch",
                    $"Patch \"{name}\" was created for master version \"{result.PatchMasterVersion}\" but the current version is \"{result.CurrentMasterVersion}\".\n" +
                    "Records or fields may have changed.",
                    new MasterMemoryDebuggerDialog.DialogButton("Cancel", () => SetStatus("Load cancelled.", false)),
                    new MasterMemoryDebuggerDialog.DialogButton("Force Load", () => ReportApply(name, MasterDataPatchService.Apply(patch, force: true)), isDanger: true));
                return;
            }
            ReportApply(name, result);
        }

        void ImportPatch()
        {
            if (!MasterDataPatchImporter.TryOpenFile((fileName, text) => ImportPatchText(MasterDataPatchImporter.SuggestName(fileName), text)))
            {
                ShowPasteDialog();
            }
        }

        /// <summary>Platforms without a file picker: paste the JSON (for example an exported patch) into a text box.</summary>
        void ShowPasteDialog()
        {
            var content = new VisualElement();
            var nameField = new TextField("Name") { value = "imported" };
            nameField.AddToClassList("mm-debugger__import-name");
            var jsonField = new TextField("Patch JSON") { multiline = true };
            jsonField.AddToClassList("mm-debugger__import-json");
            content.Add(nameField);
            content.Add(jsonField);
            dialog.Show(
                "Import patch",
                "Paste the content of a patch file (for example one exported with Export).",
                content,
                new MasterMemoryDebuggerDialog.DialogButton("Cancel", null),
                new MasterMemoryDebuggerDialog.DialogButton("Import", () => ImportPatchText(nameField.value, jsonField.value), isPrimary: true));
        }

        void ImportPatchText(string name, string json)
        {
            MasterDataPatch patch;
            try
            {
                patch = MasterDataPatchImporter.Parse(json);
            }
            catch (Exception e)
            {
                SetStatus("Import failed: " + e.Message, true);
                return;
            }

            name = MasterDataPatchStorage.NormalizeName(name) ?? "imported";
            if (MasterDataPatchStorage.Exists(name))
            {
                dialog.Show(
                    "Overwrite patch",
                    $"A patch named \"{name}\" already exists. Overwrite it with the imported patch?",
                    new MasterMemoryDebuggerDialog.DialogButton("Cancel", () => SetStatus("Import cancelled.", false)),
                    new MasterMemoryDebuggerDialog.DialogButton("Overwrite", () => SaveImported(name, patch), isDanger: true));
                return;
            }
            SaveImported(name, patch);
        }

        void SaveImported(string name, MasterDataPatch patch)
        {
            try
            {
                MasterDataPatchStorage.Save(patch, name);
            }
            catch (Exception e)
            {
                SetStatus("Import failed: " + e.Message, true);
                return;
            }
            patchName.SetValueWithoutNotify(name);
            RefreshPatchList(name);
            SetStatus($"Patch \"{name}\" imported ({patch.RecordCount} records, master version {patch.MasterVersion}). Press Load Patch to apply it.", false);
        }

        void ConfirmDeletePatch()
        {
            var name = patchList.value;
            if (string.IsNullOrEmpty(name)) return;
            dialog.Show(
                "Delete patch",
                $"Delete the saved patch \"{name}\"? Current overrides are not changed.",
                new MasterMemoryDebuggerDialog.DialogButton("Cancel", null),
                new MasterMemoryDebuggerDialog.DialogButton("Delete", () =>
                {
                    MasterDataPatchStorage.Delete(name);
                    RefreshPatchList(null);
                    SetStatus($"Patch \"{name}\" deleted.", false);
                }, isDanger: true));
        }

        void ReportApply(string name, MasterDataPatchApplyResult result)
        {
            LogWarnings(result.Warnings);
            if (!result.Succeeded)
            {
                SetStatus($"Patch \"{name}\" not loaded ({result.Status}).", true);
                return;
            }
            MasterMemoryChangeLog.PatchLoaded(name, result);
            SetStatus($"Patch \"{name}\" loaded: {result.AppliedRecords} records, {result.AppliedFields} fields{WarningSuffix(result.Warnings)}", false);
        }

        void ExportPatch()
        {
            var warnings = new List<string>();
            string json;
            try
            {
                json = MasterDataPatchService.CreatePatchJson(warnings);
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Error("Export failed: " + e);
                SetStatus("Export failed: " + e.Message, true);
                return;
            }
            LogWarnings(warnings);
            var result = MasterDataPatchExporter.Export(json, MasterDataPatchStorage.CreateExportFileName(patchName.value));
            SetStatus(result.Message + WarningSuffix(warnings), !result.Succeeded);
        }

        void ConfirmResetAll()
        {
            dialog.Show(
                "Reset All",
                "Reset all MasterMemory runtime overrides?",
                new MasterMemoryDebuggerDialog.DialogButton("Cancel", null),
                new MasterMemoryDebuggerDialog.DialogButton("Reset All", () =>
                {
                    var count = MasterMemoryDebugRuntime.OverrideCount;
                    MasterMemoryDebugRuntime.ClearAllOverrides();
                    MasterMemoryChangeLog.ResetAll(count);
                    SetStatus($"{count} overrides reset.", false);
                }, isDanger: true));
        }

        // ------------------------------------------------------------------ helpers

        void ChangeScale(float delta)
        {
            var panelSettings = host.OwnedPanelSettings;
            if (panelSettings == null) return;
            Session.Scale = Mathf.Clamp((float)Math.Round(panelSettings.scale + delta, 1), MinScale, MaxScale);
            panelSettings.scale = Session.Scale;
        }

        void RefreshHeader()
        {
            versionLabel.text = "Master: " + MasterMemoryDebugRegistry.GetMasterVersion();
            changesButton.text = changes.IsVisible ? "Back to Tables" : $"Changes ({MasterMemoryDebugRuntime.OverrideCount})";
            var count = MasterMemoryDebugRuntime.OverrideCount;
            overrideCountLabel.text = count > 0 ? $"{count} overrides" : "No overrides";
            overrideCountLabel.EnableInClassList("mm-debugger__override-count--active", count > 0);
        }

        void SetStatus(string message, bool isError)
        {
            MasterMemoryDebuggerMessages.Add(isError ? MasterMemoryDebuggerMessageType.Error : MasterMemoryDebuggerMessageType.Info, message);
            statusLabel.text = message;
            statusLabel.EnableInClassList("mm-debugger__status--error", isError);
        }

        static void LogWarnings(List<string> warnings)
        {
            foreach (var warning in warnings) MasterMemoryDebugLog.Warning(warning);
        }

        static string WarningSuffix(List<string> warnings)
        {
            return warnings.Count == 0 ? string.Empty : $"  ({warnings.Count} warnings, see Console)";
        }

        void Bind(VisualElement root, string name, Action action)
        {
            var button = Required<Button>(root, name);
            button.clicked += action;
            buttons.Add((button, action));
        }

        static void SetVisible(VisualElement root, string name, bool visible)
        {
            root.Q(name).style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static T Required<T>(VisualElement root, string name) where T : VisualElement
        {
            return root.Q<T>(name) ?? throw new InvalidOperationException($"MasterMemoryDebugger.uxml is missing <{typeof(T).Name} name=\"{name}\">.");
        }
    }
}
