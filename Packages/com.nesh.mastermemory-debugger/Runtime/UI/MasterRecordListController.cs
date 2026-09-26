using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Search box, Modified Only filter and the record grid
    /// (state ●, primary key members, a Display column when the project supplies display names, then one column per member).
    /// Columns can be hidden, frozen, resized and sorted; the settings are kept per table for the play session.
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
            modifiedOnlyToggle.RegisterValueChangedCallback(OnModifiedOnlyChanged);

            grid = new MasterRecordGrid(gridHost);
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
            searchField.UnregisterValueChangedCallback(OnSearchChanged);
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
                // columns and rows change together: rows of the previous table are never bound to the new columns
                filtered.Clear();
                grid.SetContent(columns, filtered);
            }
            ApplyFilter();
            if (keepKey != null) SelectByKey(keepKey);
        }

        /// <summary>Called when overrides changed: updates values and markers, re-filters when a filter depends on values.</summary>
        public void OnOverridesChanged()
        {
            if (ModifiedOnly || !string.IsNullOrWhiteSpace(Query) || grid.SortKey != null) ApplyFilter();
            else grid.RefreshItems();
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

            // state gutter: ● for overridden records
            result.Add(new MasterGridColumn(ModifiedColumn, "●", 26, (label, record) =>
            {
                var modified = record.IsModified;
                label.text = modified ? "●" : string.Empty;
                label.EnableInClassList(ModifiedCellClass, modified);
            })
            {
                Tooltip = "Overridden",
                CellClass = StateCellClass,
                Frozen = true,
                DefaultFrozen = true,
            });

            // primary key members first, like the columns of a database table; frozen by default
            foreach (var field in table.TypeDescriptor.PrimaryKeyFields)
            {
                var column = CreateFieldColumn(field, field.Name + " (PK)", KeyCellClass);
                column.Frozen = column.DefaultFrozen = true;
                result.Add(column);
            }

            // the default display name repeats a member; only a project supplied one gets its own column
            if (table.HasCustomDisplayName)
            {
                result.Add(new MasterGridColumn(NameColumn, "Display", 170, (label, record) => label.text = record.GetDisplayName() ?? string.Empty));
            }

            foreach (var field in table.TypeDescriptor.Fields)
            {
                if (field.IsPrimaryKey) continue;
                result.Add(CreateFieldColumn(field, field.IsSecondaryKey ? field.Name + " (SK)" : field.Name, null));
            }
            return result;
        }

        static MasterGridColumn CreateFieldColumn(MasterMemoryFieldDescriptor field, string title, string cellClass)
        {
            var isNumber = IsNumber(field.Kind);
            return new MasterGridColumn(field.Name, title, field.IsSimpleValue ? (isNumber ? 90 : 130) : 160, (label, record) =>
            {
                var value = field.GetValue(record.Current);
                label.text = value == null ? "NULL" : MasterDataValueUtility.Format(value);
                label.EnableInClassList(NullCellClass, value == null);
                label.EnableInClassList(ModifiedCellClass, record.IsModified && !MasterDataValueUtility.AreEqual(value, field.GetValue(record.Original)));
            })
            {
                Tooltip = $"{field.Name} ({field.FieldType.Name})",
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
    }
}
