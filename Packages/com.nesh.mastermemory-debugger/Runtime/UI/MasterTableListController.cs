using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Left pane: registered tables in a virtualized TreeView.
    /// Tables are shown in foldable groups when the project assigned groups
    /// (<see cref="MasterMemoryDebugRegistry.SetTableGroup(string, string[])"/>), otherwise as a flat list.
    /// </summary>
    internal sealed class MasterTableListController : IDisposable
    {
        const float RowHeight = 24f;

        sealed class Node
        {
            public MasterMemoryTableGroup Group;
            public MasterMemoryTableDescriptor Table;
        }

        readonly TreeView treeView;
        readonly Dictionary<MasterMemoryTableDescriptor, int> idByTable = new Dictionary<MasterMemoryTableDescriptor, int>();
        readonly HashSet<string> collapsedGroups = new HashSet<string>(StringComparer.Ordinal);
        readonly List<int> groupIds = new List<int>();
        readonly List<string> groupNames = new List<string>();

        public event Action<MasterMemoryTableDescriptor> TableSelected;

        public MasterMemoryTableDescriptor SelectedTable { get; private set; }

        public MasterTableListController(TreeView treeView)
        {
            this.treeView = treeView;
            treeView.fixedItemHeight = RowHeight;
            treeView.selectionType = SelectionType.Single;
            treeView.makeItem = MakeItem;
            treeView.bindItem = BindItem;
            treeView.selectionChanged += OnSelectionChanged;
        }

        public void Dispose()
        {
            treeView.selectionChanged -= OnSelectionChanged;
        }

        /// <summary>Reloads the table list from the registry and keeps the selection by name.</summary>
        public void Reload(string preferredTableName = null)
        {
            RememberCollapsedGroups();
            var selectedName = preferredTableName ?? SelectedTable?.TableName;

            var groups = MasterMemoryDebugRegistry.GetGroupedTables();
            var showGroups = groups.Count > 1 || (groups.Count == 1 && !groups[0].IsUngrouped);

            idByTable.Clear();
            groupIds.Clear();
            groupNames.Clear();
            var roots = new List<TreeViewItemData<Node>>();
            var nextTableId = 1;
            for (var g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                var children = new List<TreeViewItemData<Node>>(group.Tables.Count);
                foreach (var table in group.Tables)
                {
                    var id = nextTableId++;
                    idByTable[table] = id;
                    children.Add(new TreeViewItemData<Node>(id, new Node { Table = table }));
                }

                if (showGroups)
                {
                    var groupId = -(g + 1);
                    groupIds.Add(groupId);
                    groupNames.Add(group.Name);
                    roots.Add(new TreeViewItemData<Node>(groupId, new Node { Group = group }, children));
                }
                else
                {
                    roots.AddRange(children);
                }
            }

            treeView.SetRootItems(roots);
            treeView.Rebuild();
            for (var i = 0; i < groupIds.Count; i++)
            {
                if (!collapsedGroups.Contains(groupNames[i])) treeView.ExpandItem(groupIds[i], false, false);
            }
            treeView.RefreshItems();

            var tables = idByTable.Keys.ToList();
            var target = selectedName == null ? null : tables.FirstOrDefault(x => x.TableName == selectedName);
            if (target == null && SelectedTable == null) target = groups.SelectMany(x => x.Tables).FirstOrDefault();

            if (target != null)
            {
                treeView.SetSelectionByIdWithoutNotify(new[] { idByTable[target] });
                // notify when the selected table instance changed (first selection or re-registration)
                if (target != SelectedTable)
                {
                    SelectedTable = target;
                    TableSelected?.Invoke(SelectedTable);
                }
            }
            else if (SelectedTable != null && !idByTable.ContainsKey(SelectedTable))
            {
                treeView.ClearSelection();
                SelectedTable = null;
                TableSelected?.Invoke(null);
            }
        }

        /// <summary>Selects a table without raising <see cref="TableSelected"/> (cancelled change, or selection driven by the caller).</summary>
        public void RestoreSelection(MasterMemoryTableDescriptor table)
        {
            SelectedTable = table;
            if (table != null && idByTable.TryGetValue(table, out var id)) treeView.SetSelectionByIdWithoutNotify(new[] { id });
            else treeView.ClearSelection();
        }

        /// <summary>Refreshes the modified counts.</summary>
        public void RefreshCounts()
        {
            treeView.RefreshItems();
        }

        void RememberCollapsedGroups()
        {
            for (var i = 0; i < groupIds.Count; i++)
            {
                if (treeView.IsExpanded(groupIds[i])) collapsedGroups.Remove(groupNames[i]);
                else collapsedGroups.Add(groupNames[i]);
            }
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
            var node = treeView.GetItemDataForIndex<Node>(index);
            var nameLabel = element.Q<Label>("name");
            var countLabel = element.Q<Label>("count");

            if (node.Group != null)
            {
                var modified = node.Group.Tables.Sum(x => MasterMemoryDebugRuntime.Store.CountOf(x.RecordType));
                nameLabel.text = $"{node.Group.Name} ({node.Group.Tables.Count})";
                element.tooltip = null;
                countLabel.text = modified > 0 ? "*" + modified : string.Empty;
                element.AddToClassList("mm-debugger__table-row--group");
                element.EnableInClassList("mm-debugger__table-row--modified", modified > 0);
                return;
            }

            var table = node.Table;
            var count = MasterMemoryDebugRuntime.Store.CountOf(table.RecordType);
            nameLabel.text = MasterMemoryDebugLocalization.GetTableLabel(table);
            element.tooltip = MasterMemoryDebugLocalization.GetTableTooltip(table);
            countLabel.text = count > 0 ? "*" + count : string.Empty;
            element.RemoveFromClassList("mm-debugger__table-row--group");
            element.EnableInClassList("mm-debugger__table-row--modified", count > 0);
        }

        void OnSelectionChanged(IEnumerable<object> selection)
        {
            var node = selection.FirstOrDefault() as Node;
            if (node == null) return;

            if (node.Group != null)
            {
                // clicking a group row folds / unfolds it and keeps the table selection
                var id = -(groupNames.IndexOf(node.Group.Name) + 1);
                if (treeView.IsExpanded(id)) treeView.CollapseItem(id);
                else treeView.ExpandItem(id);
                if (SelectedTable != null && idByTable.TryGetValue(SelectedTable, out var selectedId))
                {
                    treeView.SetSelectionByIdWithoutNotify(new[] { selectedId });
                }
                else
                {
                    treeView.ClearSelection();
                }
                return;
            }

            if (node.Table == SelectedTable) return;
            SelectedTable = node.Table;
            TableSelected?.Invoke(node.Table);
        }
    }
}
