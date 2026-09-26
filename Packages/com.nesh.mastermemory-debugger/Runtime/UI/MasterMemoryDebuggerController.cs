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
            "mm-table-list", "mm-search-toolbar", "mm-search", "mm-search-completion", "mm-modified-only", "mm-record-grid", "mm-record-count", "mm-columns", "mm-columns-popup",
            "mm-inspector-title", "mm-record-state", "mm-inspector", "mm-apply", "mm-revert", "mm-reset-record", "mm-copy-json",
            "mm-close", "mm-language", "mm-table-tabs", "mm-tab-data", "mm-tab-changes", "mm-tab-patches", "mm-patches-panel",
            "mm-scale-down", "mm-scale-up", "mm-main", "mm-changes-panel", "mm-changes-list", "mm-changes-summary",
            "mm-log", "mm-log-toggle",
        };

        const float MinScale = 0.5f;
        const float MaxScale = 2f;
        const float ScaleStep = 0.1f;

        enum Tab
        {
            Data,
            Changes,
            Patches,
        }

        // Kept across open / close so the debugger reopens where it was.
        static class Session
        {
            public static string TableName;
            public static object RecordKey;
            public static string Query;
            public static bool ModifiedOnly;
            public static float Scale = 1f;
            public static string PatchName;
            public static Tab Tab = Tab.Data;
        }

        readonly MasterMemoryDebuggerDocument host;
        readonly VisualElement root;
        readonly VisualElement mainPanel;
        readonly VisualElement patchesPanel;
        readonly Button dataTab;
        readonly Button changesTab;
        readonly Button patchesTab;
        readonly MasterChangesController changes;
        readonly MasterPatchesController patches;
        Tab currentTab;
        readonly ScrollView logView;
        readonly Button logToggle;
        MasterMemoryTableDescriptor shownTable;
        readonly MasterTableListController tableList;
        readonly MasterRecordListController recordList;
        readonly MasterTableTabsController tableTabs;
        readonly DropdownField languageField;
        readonly MasterSearchCompletionController searchCompletion;
        readonly MasterRecordEditorController editor;
        readonly MasterMemoryDebuggerDialog dialog;
        readonly Label statusLabel;
        readonly Label versionLabel;
        readonly Label overrideCountLabel;
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

            tableList = new MasterTableListController(Required<TreeView>(root, "mm-table-list"));
            recordList = new MasterRecordListController(
                Required<TextField>(root, "mm-search"),
                Required<Toggle>(root, "mm-modified-only"),
                Required<VisualElement>(root, "mm-record-grid"),
                Required<Label>(root, "mm-record-count"),
                Required<Button>(root, "mm-columns"),
                Required<VisualElement>(root, "mm-columns-popup"));
            searchCompletion = new MasterSearchCompletionController(
                Required<TextField>(root, "mm-search"),
                Required<VisualElement>(root, "mm-search-completion"),
                Required<VisualElement>(root, "mm-search-toolbar"),
                () => recordList.TypeDescriptor,
                recordList.FindFieldLabel);
            editor = new MasterRecordEditorController(
                Required<Label>(root, "mm-inspector-title"),
                Required<Label>(root, "mm-record-state"),
                Required<ScrollView>(root, "mm-inspector"),
                Required<Button>(root, "mm-apply"),
                Required<Button>(root, "mm-revert"),
                Required<Button>(root, "mm-reset-record"),
                Required<Button>(root, "mm-copy-json"),
                SetStatus);

            tableTabs = new MasterTableTabsController(Required<VisualElement>(root, "mm-table-tabs"), () => shownTable, SelectTableFromTab);
            languageField = Required<DropdownField>(root, "mm-language");
            languageField.RegisterValueChangedCallback(OnLanguageSelected);
            RefreshLanguageChoices();

            mainPanel = Required<VisualElement>(root, "mm-main");
            patchesPanel = Required<VisualElement>(root, "mm-patches-panel");
            dataTab = Required<Button>(root, "mm-tab-data");
            changesTab = Required<Button>(root, "mm-tab-changes");
            patchesTab = Required<Button>(root, "mm-tab-patches");
            changes = new MasterChangesController(
                Required<VisualElement>(root, "mm-changes-panel"),
                Required<ScrollView>(root, "mm-changes-list"),
                Required<Label>(root, "mm-changes-summary"),
                OpenRecord,
                SetStatus);
            patches = new MasterPatchesController(patchesPanel, dialog, SetStatus, Session.PatchName);
            logView = Required<ScrollView>(root, "mm-log");
            logToggle = Required<Button>(root, "mm-log-toggle");
            logView.style.display = DisplayStyle.None;

            Bind(root, "mm-close", RuntimeMasterMemoryDebugger.Close);
            Bind(root, "mm-tab-data", () => SelectTab(Tab.Data));
            Bind(root, "mm-tab-changes", () => SelectTab(Tab.Changes));
            Bind(root, "mm-tab-patches", () => SelectTab(Tab.Patches));
            Bind(root, "mm-log-toggle", ToggleLog);
            Bind(root, "mm-scale-down", () => ChangeScale(-ScaleStep));
            Bind(root, "mm-scale-up", () => ChangeScale(ScaleStep));

            var canScale = host.OwnedPanelSettings != null;
            SetVisible(root, "mm-scale-down", canScale);
            SetVisible(root, "mm-scale-up", canScale);
            if (canScale) host.OwnedPanelSettings.scale = Session.Scale;

            tableList.TableSelected += OnTableSelected;
            recordList.RecordSelected += OnRecordSelected;
            editor.ReferenceRequested += OpenReference;
            MasterMemoryDebugRegistry.TablesChanged += OnTablesChanged;
            MasterMemoryDebugRuntime.OverridesChanged += OnOverridesChanged;
            MasterMemoryDebuggerMessages.Changed += OnMessagesChanged;
            MasterMemoryDebugLocalization.Changed += OnLabelsChanged;
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            // restore the previous session
            recordList.SetState(Session.Query, Session.ModifiedOnly);
            var pendingKey = Session.RecordKey;
            tableList.Reload(Session.TableName);
            if (pendingKey != null && tableList.SelectedTable != null) recordList.SelectByKey(pendingKey);

            SelectTab(Session.Tab);
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
            Session.PatchName = patches.SelectedName;
            Session.Tab = currentTab;

            MasterMemoryDebugRegistry.TablesChanged -= OnTablesChanged;
            MasterMemoryDebugRuntime.OverridesChanged -= OnOverridesChanged;
            MasterMemoryDebuggerMessages.Changed -= OnMessagesChanged;
            MasterMemoryDebugLocalization.Changed -= OnLabelsChanged;
            languageField.UnregisterValueChangedCallback(OnLanguageSelected);
            root.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            tableList.TableSelected -= OnTableSelected;
            recordList.RecordSelected -= OnRecordSelected;
            editor.ReferenceRequested -= OpenReference;
            foreach (var (button, action) in buttons) button.clicked -= action;
            buttons.Clear();

            tableList.Dispose();
            recordList.Dispose();
            searchCompletion.Dispose();
            editor.Dispose();
            patches.Dispose();
            tableTabs.Dispose();
        }

        // ------------------------------------------------------------------ events

        void OnTablesChanged()
        {
            tableList.Reload();
            tableTabs.Refresh();
            RefreshHeader();
        }

        /// <summary>A pinned tab was clicked.</summary>
        void SelectTableFromTab(MasterMemoryTableDescriptor table)
        {
            if (table == shownTable && currentTab == Tab.Data) return;
            RunAfterEditGuard(() =>
            {
                SelectTab(Tab.Data);
                if (table == shownTable) return;
                tableList.RestoreSelection(table);
                ShowTable(table);
            }, () => { });
        }

        // ------------------------------------------------------------------ labels (language)

        const string CodeNamesChoice = "Code names";

        void RefreshLanguageChoices()
        {
            var languages = MasterMemoryDebugLocalization.Languages;
            var choices = new List<string> { CodeNamesChoice };
            choices.AddRange(languages);
            languageField.choices = choices;
            var language = MasterMemoryDebugLocalization.Language;
            languageField.SetValueWithoutNotify(string.IsNullOrEmpty(language) || !choices.Contains(language) ? CodeNamesChoice : language);
            languageField.style.display = languages.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void OnLanguageSelected(ChangeEvent<string> evt)
        {
            MasterMemoryDebugLocalization.Language = evt.newValue == CodeNamesChoice ? MasterMemoryDebugLocalization.CodeNames : evt.newValue;
        }

        void OnLabelsChanged()
        {
            RefreshLanguageChoices();
            tableList.RefreshCounts();
            recordList.RefreshLabels();
            editor.RefreshLabels();
            tableTabs.Refresh();
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
            searchCompletion.Refresh();
            tableTabs.Refresh();
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
            patches.RefreshCurrent();
            RefreshHeader();
        }

        // ------------------------------------------------------------------ tabs

        void SelectTab(Tab tab)
        {
            currentTab = tab;
            mainPanel.style.display = tab == Tab.Data ? DisplayStyle.Flex : DisplayStyle.None;
            if (tab == Tab.Changes) changes.Show();
            else changes.Hide();
            patchesPanel.style.display = tab == Tab.Patches ? DisplayStyle.Flex : DisplayStyle.None;
            if (tab == Tab.Patches) patches.Refresh();
            recordList.CloseColumnsPopup();
            RefreshHeader();
        }

        void HideChanges() => SelectTab(Tab.Data);

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

        /// <summary>Opens the referenced record, or the target table filtered by the referenced member.</summary>
        void OpenReference(MasterMemoryReference reference, object value)
        {
            if (!MasterMemoryDebugRegistry.TryGetTable(reference.TargetType, out var target))
            {
                SetStatus($"{reference.TargetType.Name} is not registered.", true);
                return;
            }

            var keys = target.TypeDescriptor.PrimaryKeyFields;
            if (keys.Count == 1 && keys[0].Name == reference.TargetMember && TryConvertKey(value, target.KeyType, out var key))
            {
                if (target.TryFindOriginal(key, out _) || MasterMemoryDebugRuntime.Store.TryGet(target.RecordType, key, out _))
                {
                    OpenRecord(target, key);
                    return;
                }
                SetStatus($"{reference}: {MasterDataValueUtility.Format(value)} does not exist in {target.TableName}.", true);
                return;
            }

            // not the primary key: show every record whose member has this value
            var query = reference.TargetMember + "=" + FormatQueryValue(value);
            RunAfterEditGuard(() =>
            {
                HideChanges();
                recordList.SetState(query, false);
                tableList.RestoreSelection(target);
                ShowTable(target);
            }, () => { });
        }

        static bool TryConvertKey(object value, Type keyType, out object key)
        {
            key = value;
            if (value == null) return false;
            if (keyType.IsInstanceOfType(value)) return true;
            try
            {
                key = Convert.ChangeType(value, keyType, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static string FormatQueryValue(object value)
        {
            if (value == null) return "null";
            var text = MasterDataValueUtility.Format(value);
            return text.IndexOf(' ') >= 0 ? "\"" + text.Replace("\"", string.Empty) + "\"" : text;
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
                    else if (searchCompletion.IsOpen) searchCompletion.Close();
                    else if (recordList.IsColumnsPopupOpen) recordList.CloseColumnsPopup();
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
                    else if (target != null && editor.Container.Contains(target))
                    {
                        // string editors are multiline (to wrap long values): Enter applies instead of adding a line break
                        var textField = target as TextField ?? target.GetFirstAncestorOfType<TextField>();
                        var inTextEditor = textField != null && textField.ClassListContains("mm-debugger__text-editor");
                        var wasDirty = editor.IsDirty;
                        if (wasDirty) editor.TryApply();
                        if (wasDirty || inTextEditor) evt.StopPropagation();
                    }
                    break;
            }
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
            versionLabel.tooltip = versionLabel.text;
            var count = MasterMemoryDebugRuntime.OverrideCount;
            changesTab.text = $"Changes ({count})";
            patchesTab.text = $"Patches ({patches.PatchCount})";
            dataTab.EnableInClassList("mm-debugger__tab--selected", currentTab == Tab.Data);
            changesTab.EnableInClassList("mm-debugger__tab--selected", currentTab == Tab.Changes);
            patchesTab.EnableInClassList("mm-debugger__tab--selected", currentTab == Tab.Patches);
            overrideCountLabel.text = count > 0 ? $"{count} overrides" : "No overrides";
            overrideCountLabel.EnableInClassList("mm-debugger__override-count--active", count > 0);
        }

        void SetStatus(string message, bool isError)
        {
            MasterMemoryDebuggerMessages.Add(isError ? MasterMemoryDebuggerMessageType.Error : MasterMemoryDebuggerMessageType.Info, message);
            statusLabel.text = message;
            statusLabel.EnableInClassList("mm-debugger__status--error", isError);
        }

        internal static void LogWarnings(List<string> warnings)
        {
            foreach (var warning in warnings) MasterMemoryDebugLog.Warning(warning);
        }

        internal static string WarningSuffix(List<string> warnings)
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
