using System;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// "Changes" view: every overridden record across all tables with its changed fields (original → current).
    /// Open jumps to the record, Reset removes the override. Overrides without an original record are flagged.
    /// Rebuilt only while visible.
    /// </summary>
    internal sealed class MasterChangesController
    {
        readonly VisualElement panel;
        readonly ScrollView list;
        readonly Label summaryLabel;
        readonly Action<MasterMemoryTableDescriptor, object> open;
        readonly Action<string, bool> setStatus;

        public MasterChangesController(VisualElement panel, ScrollView list, Label summaryLabel, Action<MasterMemoryTableDescriptor, object> open, Action<string, bool> setStatus,
            Button copyButton = null)
        {
            if (copyButton != null) copyButton.clicked += CopyTsv;
            this.panel = panel;
            this.list = list;
            this.summaryLabel = summaryLabel;
            this.open = open;
            this.setStatus = setStatus;
            Hide();
        }

        public bool IsVisible => panel.style.display.value == DisplayStyle.Flex;

        public void Show()
        {
            panel.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Hide()
        {
            panel.style.display = DisplayStyle.None;
            list.Clear();
        }

        public void Refresh()
        {
            if (!IsVisible) return;
            list.Clear();

            var entries = MasterMemoryChangeSummary.Build();
            var fieldCount = 0;
            var problemCount = 0;
            foreach (var entry in entries)
            {
                fieldCount += entry.Changes.Count;
                if (entry.Status != MasterMemoryChangeStatus.Changed) problemCount++;
                list.Add(CreateEntry(entry));
            }

            if (entries.Count == 0)
            {
                var empty = new Label("No overrides. Every value comes from MasterMemory.");
                empty.AddToClassList("mm-debugger__hint");
                list.Add(empty);
            }

            var summary = $"{entries.Count} records, {fieldCount} fields changed";
            if (problemCount > 0) summary += $", {problemCount} need attention";
            summaryLabel.text = summary;
        }

        void CopyTsv()
        {
            var entries = MasterMemoryChangeSummary.Build();
            var fields = 0;
            foreach (var entry in entries) fields += entry.Changes.Count;
            if (fields == 0)
            {
                setStatus("No changed field to copy.", true);
                return;
            }
            var result = MasterDataPatchExporter.CopyToClipboard(MasterMemoryChangeSummary.ToTsv(entries), "changes.tsv", "text/tab-separated-values");
            setStatus($"Changes: {fields} fields of {entries.Count} records. {result.Message}", !result.Succeeded);
        }

        VisualElement CreateEntry(MasterMemoryChangeEntry entry)
        {
            var root = new VisualElement();
            root.AddToClassList("mm-debugger__change");
            root.EnableInClassList("mm-debugger__change--problem", entry.Status != MasterMemoryChangeStatus.Changed);

            var header = new VisualElement();
            header.AddToClassList("mm-debugger__change-header");
            var title = new Label(entry.TableName);
            title.AddToClassList("mm-debugger__change-table");
            header.Add(title);
            var key = new Label(entry.KeyText);
            key.AddToClassList("mm-debugger__change-key");
            header.Add(key);
            var name = new Label(entry.DisplayName ?? string.Empty);
            name.AddToClassList("mm-debugger__change-name");
            header.Add(name);

            if (entry.Status == MasterMemoryChangeStatus.Changed)
            {
                var openButton = new Button(() => open(entry.Table, entry.PrimaryKey)) { text = "Open" };
                openButton.AddToClassList("mm-debugger__button");
                header.Add(openButton);
            }
            var resetButton = new Button(() => Reset(entry)) { text = "Reset" };
            resetButton.AddToClassList("mm-debugger__button");
            header.Add(resetButton);
            root.Add(header);

            switch (entry.Status)
            {
                case MasterMemoryChangeStatus.OriginalMissing:
                    root.Add(CreateProblem("The original record does not exist (removed from the master data?). Gameplay still receives this override."));
                    break;
                case MasterMemoryChangeStatus.TableNotRegistered:
                    root.Add(CreateProblem("The table of this record type is not registered, the original can not be compared."));
                    break;
                default:
                    if (entry.Changes.Count == 0) root.Add(CreateProblem("No field differs from the original."));
                    foreach (var change in entry.Changes) root.Add(CreateChange(change));
                    break;
            }
            return root;
        }

        static VisualElement CreateChange(MasterDataFieldChange change)
        {
            var row = new VisualElement();
            row.AddToClassList("mm-debugger__change-field");
            var name = new Label(change.Name);
            name.AddToClassList("mm-debugger__change-field-name");
            var oldValue = new Label(MasterDataValueUtility.Format(change.OldValue));
            oldValue.AddToClassList("mm-debugger__change-old");
            var arrow = new Label("→");
            arrow.AddToClassList("mm-debugger__change-arrow");
            var newValue = new Label(MasterDataValueUtility.Format(change.NewValue));
            newValue.AddToClassList("mm-debugger__change-new");
            row.Add(name);
            row.Add(oldValue);
            row.Add(arrow);
            row.Add(newValue);
            return row;
        }

        static Label CreateProblem(string text)
        {
            var label = new Label(text);
            label.AddToClassList("mm-debugger__change-problem");
            return label;
        }

        void Reset(MasterMemoryChangeEntry entry)
        {
            using (MasterMemoryDebugHistory.Record($"Reset {entry.TableName} {entry.KeyText}"))
            {
                if (!MasterMemoryDebugRuntime.Store.Remove(entry.RecordType, entry.PrimaryKey)) return;
            }
            if (entry.Table != null && entry.Original != null)
            {
                MasterMemoryChangeLog.Removed(entry.Table, entry.PrimaryKey, entry.Current, entry.Original, "reset");
            }
            setStatus($"Override reset: {entry.TableName} {entry.KeyText}", false);
            // the store raises OverridesChanged, which refreshes this view
        }
    }
}
