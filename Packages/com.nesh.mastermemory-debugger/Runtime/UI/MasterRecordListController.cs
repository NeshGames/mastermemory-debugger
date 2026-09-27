using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Search box, Modified Only filter and the record grid
    /// (state ●, primary key members, a Display column when the project supplies display names, then one column per member).
    /// Columns can be hidden, frozen, resized and sorted; the settings are kept per table (PlayerPrefs).
    /// Results are rebuilt only when the query, the filter, the sorting, the table or the overrides change.
    /// </summary>
    internal sealed class MasterRecordListController : IDisposable
    {
        const long SearchDelayMs = 150;
        internal const string KeyColumn = "mm-key";
        internal const string NameColumn = "mm-name";
        internal const string ModifiedColumn = "mm-modified";
        const string ModifiedCellClass = "mm-debugger__cell--modified";
        const string NumberCellClass = "mm-debugger__cell--number";
        const string NullCellClass = "mm-debugger__cell--null";
        const string KeyCellClass = "mm-debugger__cell--key";
        const string StateCellClass = "mm-debugger__cell--state";
        const string AddedCellClass = "mm-debugger__cell--added";
        const string DeletedCellClass = "mm-debugger__cell--deleted";

        readonly TextField searchField;
        readonly Toggle modifiedOnlyToggle;
        readonly MasterRecordGrid grid;
        readonly MasterGridColumnsPopup columnsPopup;
        readonly Label countLabel;
        readonly IVisualElementScheduledItem filterJob;
        readonly List<MasterMemoryRecordDescriptor> filtered = new List<MasterMemoryRecordDescriptor>();

        MasterMemoryTableDescriptor table;
        List<MasterMemoryRecordDescriptor> snapshot = new List<MasterMemoryRecordDescriptor>();
        List<MasterGridColumn> columns = new List<MasterGridColumn>();

        public event Action<MasterMemoryRecordDescriptor> RecordSelected;

        public MasterMemoryRecordDescriptor SelectedRecord { get; private set; }

        public string Query => searchField.value ?? string.Empty;

        public bool ModifiedOnly => modifiedOnlyToggle.value;

        /// <summary>Members of the shown table, used by the search conditions and their completion.</summary>
        public MasterDataTypeDescriptor TypeDescriptor => table?.TypeDescriptor;

        /// <summary>Label of a field of the shown table in the selected language, or null.</summary>
        public string FindFieldLabel(string fieldName) => table == null ? null : MasterMemoryDebugLocalization.FindFieldLabel(table, fieldName);

        public bool IsColumnsPopupOpen => columnsPopup != null && columnsPopup.IsOpen;

        internal IReadOnlyList<MasterGridColumn> Columns => columns;

        internal IReadOnlyList<MasterMemoryRecordDescriptor> Rows => filtered;

        public MasterRecordListController(TextField searchField, Toggle modifiedOnlyToggle, VisualElement gridHost, Label countLabel,
            Button columnsButton = null, VisualElement columnsPopupElement = null)
        {
            this.searchField = searchField;
            this.modifiedOnlyToggle = modifiedOnlyToggle;
            this.countLabel = countLabel;

            searchField.textEdition.placeholder = "Search text, or conditions like  Damage>100 Element=Fire Name~ice";
            searchField.tooltip =
                "Space separated terms, all must match.\n" +
                "Text: primary key / name / string members (contains).\n" +
                "Field op Value with = != > >= < <= ~ (contains). Quote values with spaces: Name=\"Ice Blast\". Field=null matches null.\n" +
                "Field names, and enum / bool values after an operator, are suggested while typing: Up / Down select, Tab / Enter accept, Esc closes.";
            searchField.RegisterValueChangedCallback(OnSearchChanged);
            searchField.RegisterCallback<FocusOutEvent>(OnSearchFocusOut);
            searchField.RegisterCallback<KeyDownEvent>(OnSearchKeyDown);
            modifiedOnlyToggle.RegisterValueChangedCallback(OnModifiedOnlyChanged);

            grid = new MasterRecordGrid(gridHost) { AutoFit = column => MasterGridLayout.AutoFit(column, snapshot) };
            grid.ItemSelected += OnItemSelected;
            grid.SortChanged += ApplyFilter;
            grid.ColumnResized += SaveColumns;

            if (columnsButton != null && columnsPopupElement != null)
            {
                columnsPopup = new MasterGridColumnsPopup(columnsButton, columnsPopupElement, () => columns, OnColumnsChanged);
            }

            filterJob = gridHost.schedule.Execute(ApplyFilter);
            filterJob.Pause();
        }

        public void Dispose()
        {
            filterJob.Pause();
            SaveColumns();
            AddQueryToHistory();
            searchField.UnregisterValueChangedCallback(OnSearchChanged);
            searchField.UnregisterCallback<FocusOutEvent>(OnSearchFocusOut);
            searchField.UnregisterCallback<KeyDownEvent>(OnSearchKeyDown);
            modifiedOnlyToggle.UnregisterValueChangedCallback(OnModifiedOnlyChanged);
            grid.ItemSelected -= OnItemSelected;
            grid.SortChanged -= ApplyFilter;
            grid.ColumnResized -= SaveColumns;
            grid.Dispose();
            columnsPopup?.Dispose();
        }

        public void SetState(string query, bool modifiedOnly)
        {
            searchField.SetValueWithoutNotify(query ?? string.Empty);
            modifiedOnlyToggle.SetValueWithoutNotify(modifiedOnly);
        }

        public void CloseColumnsPopup() => columnsPopup?.Close();

        /// <summary>Recreates the column titles after the label language changed; visibility, freezing and widths are kept.</summary>
        public void RefreshLabels()
        {
            if (table == null) return;
            SaveColumns();
            columns = CreateColumns(table);
            MasterGridLayout.Restore(table.TableName, columns);
            MasterGridLayout.AutoFit(columns, snapshot);
            grid.SetContent(columns, filtered);
            if (columnsPopup != null && columnsPopup.IsOpen) columnsPopup.Open();
        }

        /// <summary>Takes a new snapshot of the table records, rebuilds the columns and the results.</summary>
        public void SetTable(MasterMemoryTableDescriptor newTable, object preferredKey = null)
        {
            var sameTable = newTable != null && table != null && newTable.RecordType == table.RecordType;
            if (!sameTable) SaveColumns();
            table = newTable;
            snapshot = table != null ? table.CreateRecordSnapshot() : new List<MasterMemoryRecordDescriptor>();
            var keepKey = preferredKey ?? (sameTable && SelectedRecord != null ? SelectedRecord.PrimaryKey : null);
            SelectedRecord = null;
            if (!sameTable)
            {
                columnsPopup?.Close();
                columns = CreateColumns(table);
                MasterGridLayout.Restore(table?.TableName, columns);
                MasterGridLayout.AutoFit(columns, snapshot);
                // columns and rows change together: rows of the previous table are never bound to the new columns
                filtered.Clear();
                grid.SetContent(columns, filtered);
            }
            ApplyFilter();
            if (keepKey != null) SelectByKey(keepKey);
        }

        /// <summary>
        /// Called when overrides changed: takes a new snapshot when records were added or removed, updates values and
        /// markers, re-filters when a filter depends on values.
        /// </summary>
        public void OnOverridesChanged()
        {
            if (table != null && AddedRecordsChanged())
            {
                SetTable(table);
                return;
            }
            if (ModifiedOnly || !string.IsNullOrWhiteSpace(Query) || grid.SortKey != null) ApplyFilter();
            else grid.RefreshItems();
        }

        /// <summary>True when the records added as overrides differ from those in the snapshot.</summary>
        bool AddedRecordsChanged()
        {
            var added = new HashSet<object>();
            HashSet<object> originals = null;
            foreach (var record in snapshot)
            {
                if (record.IsAdded) added.Add(record.PrimaryKey);
            }
            var store = MasterMemoryDebugRuntime.Store;
            if (store.CountOf(table.RecordType) == 0) return added.Count > 0;
            var count = 0;
            foreach (var entry in store.GetEntries(table.RecordType))
            {
                if (entry.IsDeleted || added.Contains(entry.Key.PrimaryKey))
                {
                    if (!entry.IsDeleted) count++;
                    continue;
                }
                if (originals == null)
                {
                    originals = new HashSet<object>();
                    foreach (var record in snapshot)
                    {
                        if (!record.IsAdded) originals.Add(record.PrimaryKey);
                    }
                }
                if (!originals.Contains(entry.Key.PrimaryKey)) return true;
            }
            return count != added.Count;
        }

        public bool SelectByKey(object key)
        {
            var index = filtered.FindIndex(x => Equals(x.PrimaryKey, key));
            if (index < 0) return false;
            grid.SetSelectionWithoutNotify(index);
            grid.ScrollToItem(index);
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
            grid.SetSelectionWithoutNotify(record == null ? -1 : filtered.IndexOf(record));
        }

        void OnItemSelected(int index)
        {
            if (index < 0 || index >= filtered.Count) return;
            var record = filtered[index];
            if (record == SelectedRecord) return;
            SelectedRecord = record;
            RecordSelected?.Invoke(record);
        }

        /// <summary>
        /// The shown rows and columns (frozen first, like the grid) as tab separated text with a title line, for
        /// pasting into a spreadsheet. Tabs and line breaks inside values become spaces.
        /// </summary>
        internal string BuildTsv() => BuildTsv(columns, filtered);

        internal static string BuildTsv(IReadOnlyList<MasterGridColumn> columns, IReadOnlyList<MasterMemoryRecordDescriptor> rows)
        {
            var shown = new List<MasterGridColumn>();
            var scrolled = new List<MasterGridColumn>();
            MasterGridLayout.Split(columns, shown, scrolled);
            shown.AddRange(scrolled);
            // the state column has no text
            shown.RemoveAll(x => x.Text == null);

            var text = new System.Text.StringBuilder();
            text.AppendLine(string.Join("\t", shown.Select(x => TsvCell(x.Title))));
            foreach (var record in rows)
            {
                text.AppendLine(string.Join("\t", shown.Select(x => TsvCell(CellText(x, record)))));
            }
            return text.ToString();
        }

        static string CellText(MasterGridColumn column, MasterMemoryRecordDescriptor record)
        {
            try
            {
                return column.Text(record);
            }
            catch (Exception e)
            {
                return "(" + e.GetType().Name + ")";
            }
        }

        static string TsvCell(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        }

        /// <summary>Every record of the table that matches the search and the Modified Only filter (not limited to the shown rows).</summary>
        internal List<MasterMemoryRecordDescriptor> GetAllMatches()
        {
            var result = new List<MasterMemoryRecordDescriptor>();
            if (table == null) return result;
            Filter(snapshot, MasterRecordQuery.Parse(Query, table.TypeDescriptor), ModifiedOnly, int.MaxValue, result);
            return result;
        }

        /// <summary>The shown table.</summary>
        internal MasterMemoryTableDescriptor Table => table;

        /// <summary>Table name of the shown table, for file names.</summary>
        internal string TableName => table?.TableName;

        // ------------------------------------------------------------------ columns

        void OnColumnsChanged()
        {
            grid.Relayout();
            SaveColumns();
        }

        void SaveColumns()
        {
            if (table != null) MasterGridLayout.Save(table.TableName, columns);
        }

        internal static List<MasterGridColumn> CreateColumns(MasterMemoryTableDescriptor table)
        {
            var result = new List<MasterGridColumn>();
            if (table == null) return result;

            // state gutter: ● overridden, + added, × deleted
            result.Add(new MasterGridColumn(ModifiedColumn, "●", 26, (label, record) =>
            {
                var modified = record.IsModified;
                var added = record.IsAdded;
                var deleted = modified && !added && record.IsDeleted;
                label.text = added ? "+" : deleted ? "×" : modified ? "●" : string.Empty;
                label.EnableInClassList(ModifiedCellClass, modified && !added && !deleted);
                label.EnableInClassList(AddedCellClass, added);
                label.EnableInClassList(DeletedCellClass, deleted);
            })
            {
                Tooltip = "● overridden   + added   × deleted",
                CellClass = StateCellClass,
                Locked = true,
                DefaultFrozen = true,
                AutoWidth = false,
            });

            // primary key members first, like the columns of a database table; frozen by default
            foreach (var field in table.TypeDescriptor.PrimaryKeyFields)
            {
                var column = CreateFieldColumn(table, field, " (PK)", KeyCellClass);
                // the key identifies the row: always shown and frozen
                column.Locked = column.DefaultFrozen = true;
                result.Add(column);
            }

            // the default display name repeats a member; only a project supplied one gets its own column
            if (table.HasCustomDisplayName)
            {
                result.Add(new MasterGridColumn(NameColumn, "Display", 170, (label, record) => label.text = record.GetDisplayName() ?? string.Empty)
                {
                    Text = record => record.GetDisplayName(),
                });
            }

            foreach (var field in table.TypeDescriptor.Fields)
            {
                if (field.IsPrimaryKey) continue;
                result.Add(CreateFieldColumn(table, field, field.IsSecondaryKey ? " (SK)" : string.Empty, null));
            }
            return result;
        }

        static MasterGridColumn CreateFieldColumn(MasterMemoryTableDescriptor table, MasterMemoryFieldDescriptor field, string suffix, string cellClass)
        {
            var isNumber = IsNumber(field.Kind);
            var title = MasterMemoryDebugLocalization.GetFieldLabel(table, field) + suffix;
            return new MasterGridColumn(field.Name, title, field.IsSimpleValue ? (isNumber ? 90 : 130) : 160, (label, record) =>
            {
                var current = record.Current;
                var value = current == null ? null : field.GetValue(current);
                label.text = value == null ? "NULL" : MasterDataValueUtility.Format(value);
                label.EnableInClassList(NullCellClass, value == null);
                var modified = record.IsModified;
                var added = record.IsAdded;
                var deleted = modified && !added && record.IsDeleted;
                label.EnableInClassList(ModifiedCellClass, modified && !added && !deleted && !MasterDataValueUtility.AreEqual(value, field.GetValue(record.Original)));
                label.EnableInClassList(AddedCellClass, added);
                label.EnableInClassList(DeletedCellClass, deleted);
            })
            {
                Tooltip = MasterMemoryDebugLocalization.GetFieldTooltip(table, field),
                Text = record =>
                {
                    var current = record.Current;
                    var value = current == null ? null : field.GetValue(current);
                    return value == null ? "NULL" : MasterDataValueUtility.Format(value);
                },
                // numbers are right aligned, like in database viewers
                CellClass = isNumber ? (cellClass == null ? NumberCellClass : cellClass + " " + NumberCellClass) : cellClass,
            };
        }

        static bool IsNumber(MasterDataValueKind kind)
        {
            switch (kind)
            {
                case MasterDataValueKind.Int32:
                case MasterDataValueKind.UInt32:
                case MasterDataValueKind.Int16:
                case MasterDataValueKind.UInt16:
                case MasterDataValueKind.Int64:
                case MasterDataValueKind.UInt64:
                case MasterDataValueKind.Byte:
                case MasterDataValueKind.SByte:
                case MasterDataValueKind.Single:
                case MasterDataValueKind.Double:
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ filter / sort

        void OnSearchChanged(ChangeEvent<string> evt)
        {
            filterJob.ExecuteLater(SearchDelayMs);
        }

        void OnSearchFocusOut(FocusOutEvent evt) => AddQueryToHistory();

        void OnSearchKeyDown(KeyDownEvent evt)
        {
            // Enter that the completion popup did not take
            if (evt.keyCode == UnityEngine.KeyCode.Return || evt.keyCode == UnityEngine.KeyCode.KeypadEnter) AddQueryToHistory();
        }

        /// <summary>A finished search (Enter, leaving the box, closing): remembered when it has no error.</summary>
        void AddQueryToHistory()
        {
            var query = Query.Trim();
            if (query.Length == 0 || table == null) return;
            if (MasterRecordQuery.Parse(query, table.TypeDescriptor).Errors.Count > 0) return;
            MasterSearchHistory.Add(query);
        }

        void OnModifiedOnlyChanged(ChangeEvent<bool> evt)
        {
            ApplyFilter();
        }

        void ApplyFilter()
        {
            filterJob.Pause();
            var query = MasterRecordQuery.Parse(Query, table?.TypeDescriptor);
            var max = MasterMemoryDebuggerSettings.Current.MaxSearchResults;

            var matches = Filter(snapshot, query, ModifiedOnly, int.MaxValue, filtered);
            if (grid.SortKey != null && table != null) SortBy(filtered, grid.SortKey, table, grid.SortDescending);
            if (filtered.Count > max) filtered.RemoveRange(max, filtered.Count - max);
            grid.RefreshItems();
            grid.SetSelectionWithoutNotify(SelectedRecord == null ? -1 : filtered.IndexOf(SelectedRecord));

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

        /// <summary>Sorts by a column key (member name, or the state / display column); stable, nulls first.</summary>
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
                    selector = x => x.Current == null ? null : field.GetValue(x.Current);
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
    }
}
