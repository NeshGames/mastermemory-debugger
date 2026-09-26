using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Left pane: registered tables (virtualized ListView).</summary>
    internal sealed class MasterTableListController : IDisposable
    {
        const float RowHeight = 24f;

        readonly ListView listView;
        readonly List<MasterMemoryTableDescriptor> tables = new List<MasterMemoryTableDescriptor>();

        public event Action<MasterMemoryTableDescriptor> TableSelected;

        public MasterMemoryTableDescriptor SelectedTable { get; private set; }

        public MasterTableListController(ListView listView)
        {
            this.listView = listView;
            listView.fixedItemHeight = RowHeight;
            listView.selectionType = SelectionType.Single;
            listView.itemsSource = tables;
            listView.makeItem = MakeItem;
            listView.bindItem = BindItem;
            listView.selectionChanged += OnSelectionChanged;
        }

        public void Dispose()
        {
            listView.selectionChanged -= OnSelectionChanged;
        }

        /// <summary>Reloads the table list from the registry and keeps the selection by name.</summary>
        public void Reload(string preferredTableName = null)
        {
            var selectedName = preferredTableName ?? SelectedTable?.TableName;
            tables.Clear();
            tables.AddRange(MasterMemoryDebugRegistry.Tables.OrderBy(x => x.TableName, StringComparer.Ordinal));
            listView.RefreshItems();

            var index = selectedName == null ? -1 : tables.FindIndex(x => x.TableName == selectedName);
            if (index < 0 && tables.Count > 0 && SelectedTable == null) index = 0;

            if (index >= 0)
            {
                listView.SetSelectionWithoutNotify(new[] { index });
                // notify when the selected table instance changed (first selection or re-registration)
                if (tables[index] != SelectedTable)
                {
                    SelectedTable = tables[index];
                    TableSelected?.Invoke(SelectedTable);
                }
            }
            else if (SelectedTable != null && !tables.Contains(SelectedTable))
            {
                listView.ClearSelection();
                SelectedTable = null;
                TableSelected?.Invoke(null);
            }
        }

        /// <summary>Refreshes the modified counts.</summary>
        public void RefreshCounts()
        {
            listView.RefreshItems();
        }

        VisualElement MakeItem()
        {
            var row = new VisualElement();
            row.AddToClassList("mm-debugger__table-row");
            var name = new Label { name = "name" };
            name.AddToClassList("mm-debugger__table-name");
            var count = new Label { name = "count" };
            count.AddToClassList("mm-debugger__table-count");
            row.Add(name);
            row.Add(count);
            return row;
        }

        void BindItem(VisualElement element, int index)
        {
            var table = tables[index];
            element.Q<Label>("name").text = table.TableName;
            var modified = MasterMemoryDebugRuntime.Store.CountOf(table.RecordType);
            element.Q<Label>("count").text = modified > 0 ? "*" + modified : string.Empty;
            element.EnableInClassList("mm-debugger__table-row--modified", modified > 0);
        }

        void OnSelectionChanged(IEnumerable<object> selection)
        {
            var table = selection.FirstOrDefault() as MasterMemoryTableDescriptor;
            if (table == null || table == SelectedTable) return;
            SelectedTable = table;
            TableSelected?.Invoke(table);
        }
    }
}
