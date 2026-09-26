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
            "mm-inspector-title", "mm-record-state", "mm-inspector", "mm-apply", "mm-revert", "mm-reset-record",
            "mm-close", "mm-save-patch", "mm-load-patch", "mm-export-patch", "mm-open-folder", "mm-reset-all",
            "mm-scale-down", "mm-scale-up",
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
        }

        readonly MasterMemoryDebuggerDocument host;
        readonly MasterTableListController tableList;
        readonly MasterRecordListController recordList;
        readonly MasterRecordEditorController editor;
        readonly MasterMemoryDebuggerDialog dialog;
        readonly Label statusLabel;
        readonly Label versionLabel;
        readonly Label overrideCountLabel;
        readonly List<(Button button, Action action)> buttons = new List<(Button, Action)>();

        public MasterMemoryDebuggerController(VisualElement root, MasterMemoryDebuggerDocument host)
        {
            this.host = host;
            var settings = MasterMemoryDebuggerSettings.Current;

            statusLabel = Required<Label>(root, "mm-status");
            versionLabel = Required<Label>(root, "mm-master-version");
            overrideCountLabel = Required<Label>(root, "mm-override-count");
            dialog = new MasterMemoryDebuggerDialog(Required<VisualElement>(root, "mm-dialog-layer"));

            tableList = new MasterTableListController(Required<ListView>(root, "mm-table-list"));
            recordList = new MasterRecordListController(
                Required<TextField>(root, "mm-search"),
                Required<Toggle>(root, "mm-modified-only"),
                Required<ListView>(root, "mm-record-list"),
                Required<Label>(root, "mm-record-count"));
            editor = new MasterRecordEditorController(
                Required<Label>(root, "mm-inspector-title"),
                Required<Label>(root, "mm-record-state"),
                Required<ScrollView>(root, "mm-inspector"),
                Required<Button>(root, "mm-apply"),
                Required<Button>(root, "mm-revert"),
                Required<Button>(root, "mm-reset-record"),
                SetStatus);

            Bind(root, "mm-close", RuntimeMasterMemoryDebugger.Close);
            Bind(root, "mm-save-patch", SavePatch);
            Bind(root, "mm-load-patch", LoadPatch);
            Bind(root, "mm-export-patch", ExportPatch);
            Bind(root, "mm-open-folder", MasterDataPatchExporter.RevealDataDirectory);
            Bind(root, "mm-reset-all", ConfirmResetAll);
            Bind(root, "mm-scale-down", () => ChangeScale(-ScaleStep));
            Bind(root, "mm-scale-up", () => ChangeScale(ScaleStep));

            SetVisible(root, "mm-save-patch", settings.AllowPatchSave);
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

            // restore the previous session
            recordList.SetState(Session.Query, Session.ModifiedOnly);
            var pendingKey = Session.RecordKey;
            tableList.Reload(Session.TableName);
            if (pendingKey != null && tableList.SelectedTable != null) recordList.SelectByKey(pendingKey);

            RefreshHeader();
            SetStatus(MasterMemoryDebugRegistry.Tables.Count == 0
                ? "No table registered. Call MasterMemoryDebugRegistry.RegisterDatabase / RegisterTable."
                : "Ready.", false);
        }

        public void Dispose()
        {
            Session.Query = recordList.Query;
            Session.ModifiedOnly = recordList.ModifiedOnly;
            Session.TableName = tableList.SelectedTable?.TableName;
            Session.RecordKey = editor.Record?.PrimaryKey;

            MasterMemoryDebugRegistry.TablesChanged -= OnTablesChanged;
            MasterMemoryDebugRuntime.OverridesChanged -= OnOverridesChanged;
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
            editor.Show(null);
            recordList.SetTable(table);
        }

        void OnRecordSelected(MasterMemoryRecordDescriptor record)
        {
            editor.Show(record);
        }

        void OnOverridesChanged()
        {
            tableList.RefreshCounts();
            recordList.OnOverridesChanged();
            editor.OnOverridesChanged();
            RefreshHeader();
        }

        // ------------------------------------------------------------------ patch

        void SavePatch()
        {
            try
            {
                var warnings = new List<string>();
                var patch = MasterDataPatchService.CreatePatch(warnings);
                MasterDataPatchStorage.Save(patch);
                LogWarnings(warnings);
                SetStatus($"Patch saved: {patch.RecordCount} records → {MasterDataPatchStorage.PatchPath}{WarningSuffix(warnings)}", false);
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Error("Save patch failed: " + e);
                SetStatus("Save failed: " + e.Message, true);
            }
        }

        void LoadPatch()
        {
            MasterDataPatch patch;
            try
            {
                patch = MasterDataPatchStorage.Load();
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Error("Load patch failed: " + e);
                SetStatus("Load failed: " + e.Message, true);
                return;
            }
            if (patch == null)
            {
                SetStatus("No saved patch: " + MasterDataPatchStorage.PatchPath, true);
                return;
            }

            var result = MasterDataPatchService.Apply(patch);
            if (result.Status == MasterDataPatchApplyStatus.VersionMismatch)
            {
                dialog.Show(
                    "Master version mismatch",
                    $"The patch was created for master version \"{result.PatchMasterVersion}\" but the current version is \"{result.CurrentMasterVersion}\".\n" +
                    "Records or fields may have changed.",
                    new MasterMemoryDebuggerDialog.DialogButton("Cancel", () => SetStatus("Load cancelled.", false)),
                    new MasterMemoryDebuggerDialog.DialogButton("Force Load", () => ReportApply(MasterDataPatchService.Apply(patch, force: true)), isDanger: true));
                return;
            }
            ReportApply(result);
        }

        void ReportApply(MasterDataPatchApplyResult result)
        {
            LogWarnings(result.Warnings);
            if (!result.Succeeded)
            {
                SetStatus($"Patch not loaded ({result.Status}).", true);
                return;
            }
            SetStatus($"Patch loaded: {result.AppliedRecords} records, {result.AppliedFields} fields{WarningSuffix(result.Warnings)}", false);
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
            var result = MasterDataPatchExporter.Export(json);
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
            var count = MasterMemoryDebugRuntime.OverrideCount;
            overrideCountLabel.text = count > 0 ? $"{count} overrides" : "No overrides";
            overrideCountLabel.EnableInClassList("mm-debugger__override-count--active", count > 0);
        }

        void SetStatus(string message, bool isError)
        {
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
