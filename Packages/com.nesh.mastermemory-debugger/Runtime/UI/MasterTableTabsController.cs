using System;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Tabs of the pinned tables above the record grid: click switches the table, × unpins, "+ Pin" pins the shown table.</summary>
    internal sealed class MasterTableTabsController : IDisposable
    {
        readonly VisualElement container;
        readonly Func<MasterMemoryTableDescriptor> currentTable;
        readonly Action<MasterMemoryTableDescriptor> selectTable;

        public MasterTableTabsController(VisualElement container, Func<MasterMemoryTableDescriptor> currentTable, Action<MasterMemoryTableDescriptor> selectTable)
        {
            this.container = container;
            this.currentTable = currentTable;
            this.selectTable = selectTable;
            MasterTablePins.Changed += Refresh;
            Refresh();
        }

        public void Dispose()
        {
            MasterTablePins.Changed -= Refresh;
        }

        /// <summary>Rebuilds the tabs (pins, registered tables or the shown table changed).</summary>
        public void Refresh()
        {
            container.Clear();
            var current = currentTable();
            foreach (var name in MasterTablePins.Names)
            {
                // pins of tables that are not registered (yet) are kept but not shown
                if (!MasterMemoryDebugRegistry.TryGetTable(name, out var table)) continue;
                container.Add(CreateTab(table, current != null && current.TableName == table.TableName));
            }

            if (current != null && !MasterTablePins.IsPinned(current.TableName))
            {
                var pin = new Button(() => MasterTablePins.Pin(current.TableName))
                {
                    text = "+ Pin",
                    tooltip = $"Pin {MasterMemoryDebugLocalization.GetTableLabel(current)} as a tab",
                };
                pin.AddToClassList("mm-debugger__button");
                pin.AddToClassList("mm-debugger__table-tab-pin");
                container.Add(pin);
            }
            container.style.display = container.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        VisualElement CreateTab(MasterMemoryTableDescriptor table, bool selected)
        {
            var tab = new VisualElement { tooltip = MasterMemoryDebugLocalization.GetTableTooltip(table) };
            tab.AddToClassList("mm-debugger__table-tab");
            tab.EnableInClassList("mm-debugger__table-tab--selected", selected);

            var label = new Label(MasterMemoryDebugLocalization.GetTableLabel(table));
            label.AddToClassList("mm-debugger__table-tab-label");
            tab.Add(label);
            tab.RegisterCallback<ClickEvent>(_ => selectTable(table));

            var close = new Label("×") { tooltip = "Unpin" };
            close.AddToClassList("mm-debugger__table-tab-close");
            close.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                MasterTablePins.Unpin(table.TableName);
            });
            tab.Add(close);
            return tab;
        }
    }
}
