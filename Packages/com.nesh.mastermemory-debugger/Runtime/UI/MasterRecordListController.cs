using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Search box, Modified Only filter and the virtualized record list.
    /// Results are rebuilt only when the query, the filter, the table or the overrides change.
    /// </summary>
    internal sealed class MasterRecordListController : IDisposable
    {
        const float RowHeight = 22f;
        const long SearchDelayMs = 150;

        readonly TextField searchField;
        readonly Toggle modifiedOnlyToggle;
        readonly ListView listView;
        readonly Label countLabel;
        readonly IVisualElementScheduledItem filterJob;
        readonly List<MasterMemoryRecordDescriptor> filtered = new List<MasterMemoryRecordDescriptor>();

        MasterMemoryTableDescriptor table;
        List<MasterMemoryRecordDescriptor> snapshot = new List<MasterMemoryRecordDescriptor>();

        public event Action<MasterMemoryRecordDescriptor> RecordSelected;

        public MasterMemoryRecordDescriptor SelectedRecord { get; private set; }

        public string Query => searchField.value ?? string.Empty;

        public bool ModifiedOnly => modifiedOnlyToggle.value;

        public MasterRecordListController(TextField searchField, Toggle modifiedOnlyToggle, ListView listView, Label countLabel)
        {
            this.searchField = searchField;
            this.modifiedOnlyToggle = modifiedOnlyToggle;
            this.listView = listView;
            this.countLabel = countLabel;

            searchField.textEdition.placeholder = "Search primary key / name / string fields";
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            modifiedOnlyToggle.RegisterValueChangedCallback(OnModifiedOnlyChanged);

            listView.fixedItemHeight = RowHeight;
            listView.selectionType = SelectionType.Single;
            listView.showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly;
            listView.itemsSource = filtered;
            listView.makeItem = MakeItem;
            listView.bindItem = BindItem;
            listView.selectionChanged += OnSelectionChanged;

            filterJob = listView.schedule.Execute(ApplyFilter);
            filterJob.Pause();
        }

        public void Dispose()
        {
            filterJob.Pause();
            searchField.UnregisterValueChangedCallback(OnSearchChanged);
            modifiedOnlyToggle.UnregisterValueChangedCallback(OnModifiedOnlyChanged);
            listView.selectionChanged -= OnSelectionChanged;
        }

        public void SetState(string query, bool modifiedOnly)
        {
            searchField.SetValueWithoutNotify(query ?? string.Empty);
            modifiedOnlyToggle.SetValueWithoutNotify(modifiedOnly);
        }

        /// <summary>Takes a new snapshot of the table records and rebuilds the results.</summary>
        public void SetTable(MasterMemoryTableDescriptor newTable, object preferredKey = null)
        {
            table = newTable;
            snapshot = table != null ? table.CreateRecordSnapshot() : new List<MasterMemoryRecordDescriptor>();
            var keepKey = preferredKey ?? (SelectedRecord != null && SelectedRecord.Table == table ? SelectedRecord.PrimaryKey : null);
            SelectedRecord = null;
            ApplyFilter();
            if (keepKey != null) SelectByKey(keepKey);
        }

        /// <summary>Called when overrides changed: updates markers, re-filters when Modified Only is on.</summary>
        public void OnOverridesChanged()
        {
            if (ModifiedOnly) ApplyFilter();
            else listView.RefreshItems();
        }

        public bool SelectByKey(object key)
        {
            var index = filtered.FindIndex(x => Equals(x.PrimaryKey, key));
            if (index < 0) return false;
            listView.SetSelectionWithoutNotify(new[] { index });
            listView.ScrollToItem(index);
            if (filtered[index] != SelectedRecord)
            {
                SelectedRecord = filtered[index];
                RecordSelected?.Invoke(SelectedRecord);
            }
            return true;
        }

        void OnSearchChanged(ChangeEvent<string> evt)
        {
            filterJob.ExecuteLater(SearchDelayMs);
        }

        void OnModifiedOnlyChanged(ChangeEvent<bool> evt)
        {
            ApplyFilter();
        }

        void ApplyFilter()
        {
            filterJob.Pause();
            var query = Query.Trim();
            var modifiedOnly = ModifiedOnly;
            var max = MasterMemoryDebuggerSettings.Current.MaxSearchResults;

            var matches = Filter(snapshot, query, modifiedOnly, max, filtered);
            listView.RefreshItems();

            var selectedIndex = SelectedRecord == null ? -1 : filtered.IndexOf(SelectedRecord);
            if (selectedIndex >= 0) listView.SetSelectionWithoutNotify(new[] { selectedIndex });
            else listView.ClearSelection();

            if (table == null)
            {
                countLabel.text = "No table selected";
            }
            else
            {
                var text = $"{filtered.Count} / {snapshot.Count} records";
                if (matches > filtered.Count) text += $"  (showing first {max} of {matches} matches, refine the search)";
                countLabel.text = text;
            }
        }

        /// <summary>Fills <paramref name="result"/> with at most <paramref name="max"/> matches and returns the total match count.</summary>
        internal static int Filter(IReadOnlyList<MasterMemoryRecordDescriptor> source, string query, bool modifiedOnly, int max, List<MasterMemoryRecordDescriptor> result)
        {
            result.Clear();
            var matches = 0;
            foreach (var record in source)
            {
                if (modifiedOnly && !record.IsModified) continue;
                if (!record.Matches(query)) continue;
                matches++;
                if (result.Count < max) result.Add(record);
            }
            return matches;
        }

        VisualElement MakeItem()
        {
            var row = new VisualElement();
            row.AddToClassList("mm-debugger__record-row");
            var key = new Label { name = "key" };
            key.AddToClassList("mm-debugger__record-key");
            var name = new Label { name = "name" };
            name.AddToClassList("mm-debugger__record-name");
            var modified = new Label { name = "modified" };
            modified.AddToClassList("mm-debugger__record-modified");
            row.Add(key);
            row.Add(name);
            row.Add(modified);
            return row;
        }

        void BindItem(VisualElement element, int index)
        {
            var record = filtered[index];
            var isModified = record.IsModified;
            element.Q<Label>("key").text = record.KeyText;
            element.Q<Label>("name").text = record.GetDisplayName() ?? string.Empty;
            element.Q<Label>("modified").text = isModified ? "*" : string.Empty;
            element.EnableInClassList("mm-debugger__record-row--modified", isModified);
        }

        void OnSelectionChanged(IEnumerable<object> selection)
        {
            var record = selection.FirstOrDefault() as MasterMemoryRecordDescriptor;
            if (record == null || record == SelectedRecord) return;
            SelectedRecord = record;
            RecordSelected?.Invoke(record);
        }
    }
}
