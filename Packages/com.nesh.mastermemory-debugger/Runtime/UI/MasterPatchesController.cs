using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// "Patches" tab: the current overrides (save as, export, reset) and every saved patch (preview, apply, merge,
    /// overwrite, rename, export, delete, import).
    /// </summary>
    internal sealed class MasterPatchesController : IDisposable
    {
        sealed class Entry
        {
            public string Name;
            public MasterDataPatch Patch;
            public string Error;
            public DateTime? SavedTime;
            public int FieldCount;
        }

        readonly VisualElement panel;
        readonly MasterMemoryDebuggerDialog dialog;
        readonly Action<string, bool> setStatus;
        readonly bool allowSave;
        readonly Label currentLabel;
        readonly Label listTitle;
        readonly ListView list;
        readonly Label detailTitle;
        readonly Label detailInfo;
        readonly Label detailWarning;
        readonly VisualElement detailActions;
        readonly ScrollView preview;
        readonly List<Entry> entries = new List<Entry>();
        readonly List<Button> selectionButtons = new List<Button>();

        Entry selected;

        public MasterPatchesController(VisualElement panel, MasterMemoryDebuggerDialog dialog, Action<string, bool> setStatus, string selectedName)
        {
            this.panel = panel;
            this.dialog = dialog;
            this.setStatus = setStatus;
            allowSave = MasterMemoryDebuggerSettings.Current.AllowPatchSave;
            panel.Clear();

            // ---- current overrides
            var current = new VisualElement();
            current.AddToClassList("mm-debugger__patches-current");
            currentLabel = new Label();
            currentLabel.AddToClassList("mm-debugger__patches-current-label");
            current.Add(currentLabel);
            if (allowSave)
            {
                current.Add(CreateButton("Save As…", "Save the current overrides as a named patch", SaveCurrentAs, primary: true));
                current.Add(CreateButton("Export", "Export the current overrides as JSON (changed fields with their original values)", ExportCurrent));
            }
            if (MasterMemoryDebuggerSettings.Current.AllowEditing)
            {
                current.Add(CreateButton("Reset All", "Remove every override", ConfirmResetAll, danger: true));
            }
            current.Add(CreateSpacer());
            current.Add(CreateButton("Import", "Add a patch file to the list (Editor: file dialog, WebGL: upload, other: paste)", Import));
            if (MasterDataPatchExporter.CanRevealExports)
            {
                current.Add(CreateButton("Open Folder", "Open the patch folder", MasterDataPatchExporter.RevealDataDirectory));
            }
            panel.Add(current);

            // ---- saved patches | selected patch
            var split = new TwoPaneSplitView(0, 260, TwoPaneSplitViewOrientation.Horizontal);
            split.AddToClassList("mm-debugger__patches-split");
            // the list gets about a third of the width once the panel is laid out (the divider can still be dragged)
            EventCallback<GeometryChangedEvent> sizeList = null;
            sizeList = evt =>
            {
                if (float.IsNaN(evt.newRect.width) || evt.newRect.width <= 0f) return;
                split.UnregisterCallback(sizeList);
                split.fixedPaneInitialDimension = Mathf.Clamp(evt.newRect.width * 0.32f, 200f, 440f);
            };
            split.RegisterCallback(sizeList);

            var listPanel = new VisualElement();
            listPanel.AddToClassList("mm-debugger__patches-list-panel");
            listTitle = new Label("Saved patches");
            listTitle.AddToClassList("mm-debugger__section-title");
            listPanel.Add(listTitle);
            list = new ListView
            {
                fixedItemHeight = 44,
                selectionType = SelectionType.Single,
                makeItem = MakeItem,
                bindItem = BindItem,
                itemsSource = entries,
            };
            list.AddToClassList("mm-debugger__patches-list");
            list.selectionChanged += OnSelectionChanged;
            listPanel.Add(list);
            split.Add(listPanel);

            var detail = new VisualElement();
            detail.AddToClassList("mm-debugger__patches-detail");
            detailTitle = new Label();
            detailTitle.AddToClassList("mm-debugger__section-title");
            detail.Add(detailTitle);
            detailInfo = new Label();
            detailInfo.AddToClassList("mm-debugger__patches-info");
            detail.Add(detailInfo);
            detailWarning = new Label();
            detailWarning.AddToClassList("mm-debugger__patches-warning");
            detail.Add(detailWarning);

            detailActions = new VisualElement();
            detailActions.AddToClassList("mm-debugger__patches-actions");
            if (MasterMemoryDebuggerSettings.Current.AllowEditing)
            {
                AddSelectionButton("Apply", "Replace every current override with this patch", () => ApplySelected(replace: true), primary: true);
                AddSelectionButton("Merge", "Apply this patch on top of the current overrides", () => ApplySelected(replace: false));
            }
            AddSelectionButton("Compare…", "Compare this patch with the current overrides or another patch", CompareSelected);
            if (allowSave)
            {
                AddSelectionButton("Overwrite", "Replace this patch with the current overrides", ConfirmOverwriteSelected);
                AddSelectionButton("Rename…", "Rename this patch", RenameSelected);
                AddSelectionButton("Export", "Export this patch as JSON", ExportSelected);
                AddSelectionButton("Delete", "Delete this patch (current overrides are not changed)", ConfirmDeleteSelected, danger: true);
            }
            detail.Add(detailActions);

            preview = new ScrollView(ScrollViewMode.Vertical);
            preview.AddToClassList("mm-debugger__patches-preview");
            detail.Add(preview);
            split.Add(detail);
            panel.Add(split);

            Refresh(selectedName);
        }

        /// <summary>Name of the selected saved patch.</summary>
        public string SelectedName => selected?.Name;

        public int PatchCount => entries.Count;

        public void Dispose()
        {
            list.selectionChanged -= OnSelectionChanged;
        }

        /// <summary>Reloads the saved patches and keeps <paramref name="select"/> (or the current selection) selected.</summary>
        public void Refresh(string select = null)
        {
            var keep = MasterDataPatchStorage.NormalizeName(select) ?? selected?.Name ?? MasterDataPatchStorage.DefaultPatchName;
            entries.Clear();
            foreach (var name in MasterDataPatchStorage.ListPatchNames())
            {
                var entry = new Entry { Name = name, SavedTime = MasterDataPatchStorage.GetSavedTime(name) };
                try
                {
                    entry.Patch = MasterDataPatchStorage.Load(name);
                    entry.FieldCount = CountFields(entry.Patch);
                }
                catch (Exception e)
                {
                    entry.Error = e.Message;
                }
                entries.Add(entry);
            }
            list.RefreshItems();
            listTitle.text = $"Saved patches ({entries.Count})";

            var index = entries.FindIndex(x => x.Name == keep);
            if (index < 0 && entries.Count > 0) index = 0;
            selected = index >= 0 ? entries[index] : null;
            if (index >= 0) list.SetSelectionWithoutNotify(new[] { index });
            else list.ClearSelection();
            RefreshCurrent();
            ShowSelected();
        }

        /// <summary>Called when the overrides changed.</summary>
        public void RefreshCurrent()
        {
            var count = MasterMemoryDebugRuntime.OverrideCount;
            currentLabel.text = count == 0 ? "Current: no overrides" : $"Current: {count} overridden records";
        }

        internal static int CountFields(MasterDataPatch patch)
        {
            var count = 0;
            if (patch == null) return 0;
            foreach (var table in patch.Tables)
            {
                foreach (var record in table.Records) count += record.Changes.Count;
            }
            return count;
        }

        /// <summary>Compact text of a patch value (JSON).</summary>
        internal static string FormatValue(object json)
        {
            if (json is string s) return s;
            return MasterDataJson.Serialize(json, pretty: false);
        }

        // ------------------------------------------------------------------ list

        static VisualElement MakeItem()
        {
            var item = new VisualElement();
            item.AddToClassList("mm-debugger__patch-item");
            var name = new Label();
            name.AddToClassList("mm-debugger__patch-item-name");
            var info = new Label();
            info.AddToClassList("mm-debugger__patch-item-info");
            item.Add(name);
            item.Add(info);
            item.userData = (name, info);
            return item;
        }

        void BindItem(VisualElement item, int index)
        {
            var entry = entries[index];
            var (name, info) = ((Label, Label))item.userData;
            name.text = entry.Name == MasterDataPatchStorage.DefaultPatchName ? entry.Name + "  (default)" : entry.Name;
            info.text = entry.Error != null ? "unreadable: " + entry.Error : Describe(entry, compact: true);
            item.EnableInClassList("mm-debugger__patch-item--problem", entry.Error != null || !IsSameVersion(entry));
        }

        void OnSelectionChanged(IEnumerable<object> selection)
        {
            var index = list.selectedIndex;
            selected = index >= 0 && index < entries.Count ? entries[index] : null;
            ShowSelected();
        }

        static string Describe(Entry entry, bool compact)
        {
            var patch = entry.Patch;
            if (patch == null) return string.Empty;
            var time = entry.SavedTime?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-";
            return compact
                ? $"{patch.RecordCount} records · {entry.FieldCount} fields · {time}"
                : $"{patch.RecordCount} records, {entry.FieldCount} fields   ·   master version {patch.MasterVersion ?? "-"}   ·   saved {time}";
        }

        static bool IsSameVersion(Entry entry)
        {
            return entry.Patch == null || MasterDataPatchService.IsSameMasterVersion(entry.Patch.MasterVersion, MasterMemoryDebugRegistry.GetMasterVersion());
        }

        // ------------------------------------------------------------------ detail

        void ShowSelected()
        {
            preview.Clear();
            foreach (var button in selectionButtons) button.SetEnabled(selected != null && selected.Patch != null);
            if (selected == null)
            {
                detailTitle.text = entries.Count == 0 ? "No saved patches" : "Select a patch";
                detailInfo.text = entries.Count == 0 ? "Save the current overrides with Save As…, or Import a patch file." : string.Empty;
                detailWarning.text = string.Empty;
                return;
            }

            detailTitle.text = selected.Name;
            if (selected.Patch == null)
            {
                detailInfo.text = string.Empty;
                detailWarning.text = "This file can not be read: " + selected.Error;
                return;
            }
            detailInfo.text = Describe(selected, compact: false);
            detailWarning.text = IsSameVersion(selected)
                ? string.Empty
                : $"Created for master version \"{selected.Patch.MasterVersion}\", the current version is \"{MasterMemoryDebugRegistry.GetMasterVersion()}\".";

            foreach (var table in selected.Patch.Tables)
            {
                var tableLabel = new Label($"{table.TableName}  ({table.Records.Count})");
                tableLabel.AddToClassList("mm-debugger__patches-table");
                if (!MasterMemoryDebugRegistry.TryGetTable(table.TableName, out _))
                {
                    tableLabel.text += "   not registered";
                    tableLabel.AddToClassList("mm-debugger__patches-table--missing");
                }
                preview.Add(tableLabel);

                foreach (var record in table.Records)
                {
                    var recordLabel = new Label(record.PrimaryKey.ToCanonicalString() + (record.Added ? "   added" : record.Deleted ? "   deleted" : string.Empty));
                    recordLabel.AddToClassList("mm-debugger__patches-record");
                    preview.Add(recordLabel);
                    foreach (var change in record.Changes)
                    {
                        var line = new VisualElement();
                        line.AddToClassList("mm-debugger__change-field");
                        var field = new Label(change.Field);
                        field.AddToClassList("mm-debugger__change-field-name");
                        line.Add(field);
                        if (change.HasOriginal)
                        {
                            var original = new Label(FormatValue(change.Original));
                            original.AddToClassList("mm-debugger__change-old");
                            line.Add(original);
                            var arrow = new Label("→");
                            arrow.AddToClassList("mm-debugger__change-arrow");
                            line.Add(arrow);
                        }
                        var value = new Label(FormatValue(change.Value));
                        value.AddToClassList("mm-debugger__change-new");
                        line.Add(value);
                        preview.Add(line);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ actions

        void ApplySelected(bool replace)
        {
            var entry = selected;
            if (entry?.Patch == null) return;
            var count = MasterMemoryDebugRuntime.OverrideCount;
            if (replace && count > 0)
            {
                dialog.Show(
                    "Apply patch",
                    $"Apply \"{entry.Name}\"? The {count} current overrides are replaced (use Merge to keep them).",
                    new MasterMemoryDebuggerDialog.DialogButton("Cancel", null),
                    new MasterMemoryDebuggerDialog.DialogButton("Apply", () => Apply(entry, replace: true), isPrimary: true));
                return;
            }
            Apply(entry, replace);
        }

        void Apply(Entry entry, bool replace)
        {
            var result = ApplyPatch(entry, force: false, replace);
            if (result.Status == MasterDataPatchApplyStatus.VersionMismatch)
            {
                dialog.Show(
                    "Master version mismatch",
                    $"Patch \"{entry.Name}\" was created for master version \"{result.PatchMasterVersion}\" but the current version is \"{result.CurrentMasterVersion}\".\n" +
                    "Records or fields may have changed.",
                    new MasterMemoryDebuggerDialog.DialogButton("Cancel", () => setStatus("Apply cancelled.", false)),
                    new MasterMemoryDebuggerDialog.DialogButton("Force Apply", () => Report(entry.Name, replace, ApplyPatch(entry, force: true, replace)), isDanger: true));
                return;
            }
            Report(entry.Name, replace, result);
        }

        static MasterDataPatchApplyResult ApplyPatch(Entry entry, bool force, bool replace)
        {
            using (MasterMemoryDebugHistory.Record($"{(replace ? "Apply" : "Merge")} patch \"{entry.Name}\""))
            {
                return MasterDataPatchService.Apply(entry.Patch, force, replaceExisting: replace);
            }
        }

        void Report(string name, bool replace, MasterDataPatchApplyResult result)
        {
            MasterMemoryDebuggerController.LogWarnings(result.Warnings);
            if (!result.Succeeded)
            {
                setStatus($"Patch \"{name}\" not applied ({result.Status}).", true);
                return;
            }
            MasterMemoryChangeLog.PatchLoaded(name, result);
            setStatus($"Patch \"{name}\" {(replace ? "applied" : "merged")}: {result.AppliedRecords} records, {result.AppliedFields} fields" +
                      MasterMemoryDebuggerController.WarningSuffix(result.Warnings), false);
        }

        void SaveCurrentAs()
        {
            var nameField = new TextField("Name") { value = selected?.Name ?? MasterDataPatchStorage.DefaultPatchName };
            nameField.AddToClassList("mm-debugger__import-name");
            dialog.Show(
                "Save patch",
                $"Save the {MasterMemoryDebugRuntime.OverrideCount} current overrides as a patch.",
                nameField,
                new MasterMemoryDebuggerDialog.DialogButton("Cancel", null),
                new MasterMemoryDebuggerDialog.DialogButton("Save", () => ConfirmSave(nameField.value), isPrimary: true));
        }

        void ConfirmSave(string rawName)
        {
            var name = MasterDataPatchStorage.NormalizeName(rawName);
            if (name == null)
            {
                setStatus("Enter a patch name.", true);
                return;
            }
            if (MasterDataPatchStorage.Exists(name))
            {
                dialog.Show(
                    "Overwrite patch",
                    $"A patch named \"{name}\" already exists. Overwrite it?",
                    new MasterMemoryDebuggerDialog.DialogButton("Cancel", null),
                    new MasterMemoryDebuggerDialog.DialogButton("Overwrite", () => Save(name), isDanger: true));
                return;
            }
            Save(name);
        }

        void ConfirmOverwriteSelected()
        {
            var name = selected?.Name;
            if (name == null) return;
            dialog.Show(
                "Overwrite patch",
                $"Replace \"{name}\" with the {MasterMemoryDebugRuntime.OverrideCount} current overrides?",
                new MasterMemoryDebuggerDialog.DialogButton("Cancel", null),
                new MasterMemoryDebuggerDialog.DialogButton("Overwrite", () => Save(name), isDanger: true));
        }

        void Save(string name)
        {
            try
            {
                var warnings = new List<string>();
                var patch = MasterDataPatchService.CreatePatch(warnings);
                var path = MasterDataPatchStorage.Save(patch, name);
                MasterMemoryDebuggerController.LogWarnings(warnings);
                Refresh(name);
                setStatus($"Patch \"{name}\" saved: {patch.RecordCount} records → {path}{MasterMemoryDebuggerController.WarningSuffix(warnings)}", false);
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Error("Save patch failed: " + e);
                setStatus("Save failed: " + e.Message, true);
            }
        }

        void RenameSelected()
        {
            var oldName = selected?.Name;
            if (oldName == null) return;
            var nameField = new TextField("New name") { value = oldName };
            nameField.AddToClassList("mm-debugger__import-name");
            dialog.Show(
                "Rename patch",
                $"Rename \"{oldName}\".",
                nameField,
                new MasterMemoryDebuggerDialog.DialogButton("Cancel", null),
                new MasterMemoryDebuggerDialog.DialogButton("Rename", () =>
                {
                    var newName = MasterDataPatchStorage.NormalizeName(nameField.value);
                    if (newName == null || !MasterDataPatchStorage.Rename(oldName, newName))
                    {
                        setStatus(newName == null ? "Enter a patch name." : $"A patch named \"{newName}\" already exists.", true);
                        return;
                    }
                    Refresh(newName);
                    setStatus($"Patch \"{oldName}\" renamed to \"{newName}\".", false);
                }, isPrimary: true));
        }

        void ConfirmDeleteSelected()
        {
            var name = selected?.Name;
            if (name == null) return;
            dialog.Show(
                "Delete patch",
                $"Delete the saved patch \"{name}\"? Current overrides are not changed.",
                new MasterMemoryDebuggerDialog.DialogButton("Cancel", null),
                new MasterMemoryDebuggerDialog.DialogButton("Delete", () =>
                {
                    MasterDataPatchStorage.Delete(name);
                    selected = null;
                    Refresh();
                    setStatus($"Patch \"{name}\" deleted.", false);
                }, isDanger: true));
        }

        void ExportSelected()
        {
            var entry = selected;
            if (entry?.Patch == null) return;
            var result = MasterDataPatchExporter.Export(MasterDataPatchSerializer.ToJson(entry.Patch), MasterDataPatchStorage.CreateExportFileName(entry.Name));
            setStatus(result.Message, !result.Succeeded);
        }

        void ExportCurrent()
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
                setStatus("Export failed: " + e.Message, true);
                return;
            }
            MasterMemoryDebuggerController.LogWarnings(warnings);
            var result = MasterDataPatchExporter.Export(json, MasterDataPatchStorage.CreateExportFileName("current"));
            setStatus(result.Message + MasterMemoryDebuggerController.WarningSuffix(warnings), !result.Succeeded);
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
                    using (MasterMemoryDebugHistory.Record("Reset All")) MasterMemoryDebugRuntime.ClearAllOverrides();
                    MasterMemoryChangeLog.ResetAll(count);
                    setStatus($"{count} overrides reset.", false);
                }, isDanger: true));
        }

        // ------------------------------------------------------------------ compare

        const string CurrentOverridesChoice = "Current overrides";

        void CompareSelected()
        {
            var entry = selected;
            if (entry?.Patch == null) return;
            var choices = new List<string> { CurrentOverridesChoice };
            foreach (var other in entries)
            {
                if (other != entry && other.Patch != null) choices.Add(other.Name);
            }
            var target = new DropdownField("Compare with", choices, 0);
            dialog.Show(
                "Compare patch",
                $"Lists the fields that \"{entry.Name}\" and the other side change differently.",
                target,
                new MasterMemoryDebuggerDialog.DialogButton("Cancel", null),
                new MasterMemoryDebuggerDialog.DialogButton("Compare", () =>
                {
                    var name = target.value;
                    var other = name == CurrentOverridesChoice ? MasterDataPatchService.CreatePatch() : entries.Find(x => x.Name == name)?.Patch;
                    ShowComparison(entry.Name, entry.Patch, name, other);
                }, isPrimary: true));
        }

        void ShowComparison(string nameA, MasterDataPatch a, string nameB, MasterDataPatch b)
        {
            const int MaxLines = 300;
            var differences = MasterDataPatchCompare.Compare(a, b);
            var content = new ScrollView(ScrollViewMode.Vertical);
            content.AddToClassList("mm-debugger__import-preview");

            int onlyA = 0, onlyB = 0, different = 0;
            string lastRecord = null;
            foreach (var difference in differences)
            {
                switch (difference.Kind)
                {
                    case MasterDataPatchDifferenceKind.OnlyInA: onlyA++; break;
                    case MasterDataPatchDifferenceKind.OnlyInB: onlyB++; break;
                    default: different++; break;
                }
                if (content.childCount >= MaxLines) continue;

                var record = difference.TableName + "  " + difference.Key;
                if (record != lastRecord)
                {
                    lastRecord = record;
                    var recordLabel = new Label(record);
                    recordLabel.AddToClassList("mm-debugger__patches-record");
                    content.Add(recordLabel);
                }
                var line = new VisualElement();
                line.AddToClassList("mm-debugger__change-field");
                line.Add(CreateCell(difference.Field, "mm-debugger__change-field-name"));
                line.Add(CreateCell(difference.Kind == MasterDataPatchDifferenceKind.OnlyInB ? "—" : MasterDataPatchCompare.Format(difference.ValueA), "mm-debugger__compare-a"));
                line.Add(CreateCell("|", "mm-debugger__change-arrow"));
                line.Add(CreateCell(difference.Kind == MasterDataPatchDifferenceKind.OnlyInA ? "—" : MasterDataPatchCompare.Format(difference.ValueB), "mm-debugger__compare-b"));
                if (difference.HasOriginal) line.Add(CreateCell("original " + MasterDataPatchCompare.Format(difference.Original), "mm-debugger__change-old"));
                content.Add(line);
            }
            if (content.childCount >= MaxLines) content.Add(new Label("… see Copy TSV for the whole list"));

            var summary = differences.Count == 0
                ? $"\"{nameA}\" and {Quote(nameB)} change the same fields to the same values."
                : $"{nameA} | {nameB}:  {different} different values, {onlyA} only in {nameA}, {onlyB} only in {nameB}.";
            if (differences.Count == 0)
            {
                dialog.Show("Compare patch", summary, new MasterMemoryDebuggerDialog.DialogButton("Close", null));
                return;
            }
            dialog.Show(
                "Compare patch",
                summary,
                content,
                new MasterMemoryDebuggerDialog.DialogButton("Close", null),
                new MasterMemoryDebuggerDialog.DialogButton("Copy TSV", () =>
                {
                    var result = MasterDataPatchExporter.CopyToClipboard(MasterDataPatchCompare.ToTsv(differences, nameA, nameB), "compare.tsv", "text/tab-separated-values");
                    setStatus($"Comparison of {nameA} and {nameB}: {result.Message}", !result.Succeeded);
                }));
        }

        static string Quote(string name) => name == CurrentOverridesChoice ? "the current overrides" : $"\"{name}\"";

        static Label CreateCell(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }

        // ------------------------------------------------------------------ import

        void Import()
        {
            if (!MasterDataPatchImporter.TryOpenFile((fileName, text) => ImportText(MasterDataPatchImporter.SuggestName(fileName), text)))
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
                new MasterMemoryDebuggerDialog.DialogButton("Import", () => ImportText(nameField.value, jsonField.value), isPrimary: true));
        }

        void ImportText(string name, string json)
        {
            MasterDataPatch patch;
            try
            {
                patch = MasterDataPatchImporter.Parse(json);
            }
            catch (Exception e)
            {
                setStatus("Import failed: " + e.Message, true);
                return;
            }

            name = MasterDataPatchStorage.NormalizeName(name) ?? "imported";
            if (MasterDataPatchStorage.Exists(name))
            {
                dialog.Show(
                    "Overwrite patch",
                    $"A patch named \"{name}\" already exists. Overwrite it with the imported patch?",
                    new MasterMemoryDebuggerDialog.DialogButton("Cancel", () => setStatus("Import cancelled.", false)),
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
                setStatus("Import failed: " + e.Message, true);
                return;
            }
            Refresh(name);
            setStatus($"Patch \"{name}\" imported ({patch.RecordCount} records, master version {patch.MasterVersion}). Use Apply or Merge to use it.", false);
        }

        // ------------------------------------------------------------------ helpers

        void AddSelectionButton(string text, string tooltip, Action action, bool primary = false, bool danger = false)
        {
            var button = CreateButton(text, tooltip, action, primary, danger);
            selectionButtons.Add(button);
            detailActions.Add(button);
        }

        static Button CreateButton(string text, string tooltip, Action action, bool primary = false, bool danger = false)
        {
            var button = new Button(action) { text = text, tooltip = tooltip };
            button.AddToClassList("mm-debugger__button");
            if (primary) button.AddToClassList("mm-debugger__button--primary");
            if (danger) button.AddToClassList("mm-debugger__button--danger");
            return button;
        }

        static VisualElement CreateSpacer()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList("mm-debugger__spacer");
            return spacer;
        }
    }
}
