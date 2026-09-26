using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Search box, Modified Only filter and the virtualized record table
    /// (Primary Key, Name, Mod, then one column per member; click a header to sort).
    /// Results are rebuilt only when the query, the filter, the sorting, the table or the overrides change.
    /// </summary>
    internal sealed class MasterRecordListController : IDisposable
    {
        const float RowHeight = 22f;
        const long SearchDelayMs = 150;
        const string KeyColumn = "mm-key";
        const string NameColumn = "mm-name";
        const string ModifiedColumn = "mm-modified";
        const string CellClass = "mm-debugger__cell";
        const string ModifiedCellClass = "mm-debugger__cell--modified";

        readonly TextField searchField;
        readonly Toggle modifiedOnlyToggle;
        readonly MultiColumnListView listView;
        readonly Label countLabel;
        readonly IVisualElementScheduledItem filterJob;
        readonly List<MasterMemoryRecordDescriptor> filtered = new List<MasterMemoryRecordDescriptor>();

        MasterMemoryTableDescriptor table;
        List<MasterMemoryRecordDescriptor> snapshot = new List<MasterMemoryRecordDescriptor>();

        public event Action<MasterMemoryRecordDescriptor> RecordSelected;

        public MasterMemoryRecordDescriptor SelectedRecord { get; private set; }

        public string Query => searchField.value ?? string.Empty;

        public bool ModifiedOnly => modifiedOnlyToggle.value;

        /// <summary>Members of the shown table, used by the search conditions and their completion.</summary>
        public MasterDataTypeDescriptor TypeDescriptor => table?.TypeDescriptor;

        public MasterRecordListController(TextField searchField, Toggle modifiedOnlyToggle, MultiColumnListView listView, Label countLabel)
        {
            this.searchField = searchField;
            this.modifiedOnlyToggle = modifiedOnlyToggle;
            this.listView = listView;
            this.countLabel = countLabel;

            searchField.textEdition.placeholder = "Search text, or conditions like  Damage>100 Element=Fire Name~ice";
            searchField.tooltip =
                "Space separated terms, all must match.\n" +
                "Text: primary key / name / string members (contains).\n" +
                "Field op Value with = != > >= < <= ~ (contains). Quote values with spaces: Name=\"Ice Blast\". Field=null matches null.\n" +
                "Field names, and enum / bool values after an operator, are suggested while typing: Up / Down select, Tab / Enter accept, Esc closes.";
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            modifiedOnlyToggle.RegisterValueChangedCallback(OnModifiedOnlyChanged);

            listView.fixedItemHeight = RowHeight;
            listView.selectionType = SelectionType.Single;
            listView.showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly;
            listView.sortingMode = ColumnSortingMode.Custom;
            listView.itemsSource = filtered;
            listView.columnSortingChanged += OnSortingChanged;
            listView.selectionChanged += OnSelectionChanged;

            filterJob = listView.schedule.Execute(ApplyFilter);
            filterJob.Pause();
        }

        public void Dispose()
        {
            filterJob.Pause();
            searchField.UnregisterValueChangedCallback(OnSearchChanged);
            modifiedOnlyToggle.UnregisterValueChangedCallback(OnModifiedOnlyChanged);
            listView.columnSortingChanged -= OnSortingChanged;
            listView.selectionChanged -= OnSelectionChanged;
        }

        public void SetState(string query, bool modifiedOnly)
        {
            searchField.SetValueWithoutNotify(query ?? string.Empty);
            modifiedOnlyToggle.SetValueWithoutNotify(modifiedOnly);
        }

        /// <summary>Takes a new snapshot of the table records, rebuilds the columns and the results.</summary>
        public void SetTable(MasterMemoryTableDescriptor newTable, object preferredKey = null)
        {
            var sameTable = newTable != null && table != null && newTable.RecordType == table.RecordType;
            table = newTable;
            snapshot = table != null ? table.CreateRecordSnapshot() : new List<MasterMemoryRecordDescriptor>();
            var keepKey = preferredKey ?? (sameTable && SelectedRecord != null ? SelectedRecord.PrimaryKey : null);
            SelectedRecord = null;
            if (!sameTable) RebuildColumns();
            ApplyFilter();
            if (keepKey != null) SelectByKey(keepKey);
        }

        /// <summary>Called when overrides changed: updates values and markers, re-filters when a filter depends on values.</summary>
        public void OnOverridesChanged()
        {
            if (ModifiedOnly || !string.IsNullOrWhiteSpace(Query) || listView.sortedColumns.Any()) ApplyFilter();
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

        /// <summary>Highlights a record without raising <see cref="RecordSelected"/> (used when a selection change is cancelled).</summary>
        public void RestoreSelection(MasterMemoryRecordDescriptor record)
        {
            SelectedRecord = record;
            var index = record == null ? -1 : filtered.IndexOf(record);
            if (index >= 0) listView.SetSelectionWithoutNotify(new[] { index });
            else listView.ClearSelection();
        }

        // ------------------------------------------------------------------ columns

        void RebuildColumns()
        {
            listView.sortColumnDescriptions.Clear();
            listView.columns.Clear();
            if (table != null)
            {
                listView.columns.Add(CreateColumn(KeyColumn, "Primary Key", 130, (label, record) => label.text = record.KeyText));
                listView.columns.Add(CreateColumn(NameColumn, "Name", 170, (label, record) => label.text = record.GetDisplayName() ?? string.Empty));
                listView.columns.Add(CreateColumn(ModifiedColumn, "Mod", 42, (label, record) =>
                {
                    var modified = record.IsModified;
                    label.text = modified ? "*" : string.Empty;
                    label.EnableInClassList(ModifiedCellClass, modified);
                }));

                foreach (var field in table.TypeDescriptor.Fields)
                {
                    if (field.IsPrimaryKey) continue;
                    var f = field;
                    var column = CreateColumn(field.Name, field.Name, field.IsSimpleValue ? 110 : 160, (label, record) =>
                    {
                        var value = f.GetValue(record.Current);
                        label.text = MasterDataValueUtility.Format(value);
                        label.EnableInClassList(ModifiedCellClass, record.IsModified && !MasterDataValueUtility.AreEqual(value, f.GetValue(record.Original)));
                    });
                    column.title = field.IsSecondaryKey ? field.Name + " (SK)" : field.Name;
                    listView.columns.Add(column);
                }
            }
            listView.Rebuild();
        }

        Column CreateColumn(string name, string title, float width, Action<Label, MasterMemoryRecordDescriptor> bind)
        {
            return new Column
            {
                name = name,
                title = title,
                width = width,
                minWidth = 30,
                sortable = true,
                stretchable = false,
                makeCell = () =>
                {
                    var label = new Label();
                    label.AddToClassList(CellClass);
                    return label;
                },
                bindCell = (element, index) => bind((Label)element, filtered[index]),
            };
        }

        // ------------------------------------------------------------------ filter / sort

        void OnSearchChanged(ChangeEvent<string> evt)
        {
            filterJob.ExecuteLater(SearchDelayMs);
        }

        void OnModifiedOnlyChanged(ChangeEvent<bool> evt)
        {
            ApplyFilter();
        }

        void OnSortingChanged()
        {
            ApplyFilter();
        }

        void ApplyFilter()
        {
            filterJob.Pause();
            var query = MasterRecordQuery.Parse(Query, table?.TypeDescriptor);
            var max = MasterMemoryDebuggerSettings.Current.MaxSearchResults;

            var matches = Filter(snapshot, query, ModifiedOnly, int.MaxValue, filtered);
            Sort(filtered, table, listView.sortedColumns.FirstOrDefault());
            if (filtered.Count > max) filtered.RemoveRange(max, filtered.Count - max);
            listView.RefreshItems();

            var selectedIndex = SelectedRecord == null ? -1 : filtered.IndexOf(SelectedRecord);
            if (selectedIndex >= 0) listView.SetSelectionWithoutNotify(new[] { selectedIndex });
            else listView.ClearSelection();

            UpdateCountLabel(query, matches, max);
        }

        void UpdateCountLabel(MasterRecordQuery query, int matches, int max)
        {
            if (table == null)
            {
                countLabel.text = "No table selected";
                countLabel.RemoveFromClassList("mm-debugger__record-count--error");
                return;
            }
            var text = $"{filtered.Count} / {snapshot.Count} records";
            if (matches > filtered.Count) text += $"  (showing first {max} of {matches} matches, refine the search)";
            if (query.Errors.Count > 0) text += "   ⚠ " + string.Join("; ", query.Errors) + " (ignored)";
            countLabel.text = text;
            countLabel.EnableInClassList("mm-debugger__record-count--error", query.Errors.Count > 0);
        }

        /// <summary>Fills <paramref name="result"/> with at most <paramref name="max"/> matches and returns the total match count.</summary>
        internal static int Filter(IReadOnlyList<MasterMemoryRecordDescriptor> source, MasterRecordQuery query, bool modifiedOnly, int max, List<MasterMemoryRecordDescriptor> result)
        {
            result.Clear();
            var matches = 0;
            foreach (var record in source)
            {
                if (modifiedOnly && !record.IsModified) continue;
                if (!query.Matches(record)) continue;
                matches++;
                if (result.Count < max) result.Add(record);
            }
            return matches;
        }

        /// <summary>Sorts by a column of the record table (stable; nulls first).</summary>
        internal static void Sort(List<MasterMemoryRecordDescriptor> records, MasterMemoryTableDescriptor table, SortColumnDescription sort)
        {
            if (sort == null || table == null || records.Count < 2) return;
            var descending = sort.direction == SortDirection.Descending;
            SortBy(records, sort.columnName, table, descending);
        }

        internal static void SortBy(List<MasterMemoryRecordDescriptor> records, string columnName, MasterMemoryTableDescriptor table, bool descending)
        {
            Func<MasterMemoryRecordDescriptor, object> selector;
            switch (columnName)
            {
                case KeyColumn:
                    selector = x => x.PrimaryKey;
                    break;
                case NameColumn:
                    selector = x => x.GetDisplayName();
                    break;
                case ModifiedColumn:
                    selector = x => x.IsModified;
                    break;
                default:
                    if (!table.TypeDescriptor.TryGetField(columnName, out var field)) return;
                    selector = x => field.GetValue(x.Current);
                    break;
            }

            var keyed = records.Select((record, index) => (record, key: selector(record), index)).ToList();
            keyed.Sort((a, b) =>
            {
                var c = CompareValues(a.key, b.key);
                if (descending) c = -c;
                return c != 0 ? c : a.index.CompareTo(b.index);
            });
            for (var i = 0; i < keyed.Count; i++) records[i] = keyed[i].record;
        }

        static int CompareValues(object a, object b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            if (a.GetType() == b.GetType() && a is IComparable comparable)
            {
                try
                {
                    return comparable.CompareTo(b);
                }
                catch (ArgumentException)
                {
                    // fall through to text comparison
                }
            }
            return string.Compare(MasterDataValueUtility.Format(a), MasterDataValueUtility.Format(b), StringComparison.OrdinalIgnoreCase);
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
