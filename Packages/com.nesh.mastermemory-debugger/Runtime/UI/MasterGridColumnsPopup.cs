using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>"Columns" popup of the record grid: show / hide and freeze each column, Show All, Reset.</summary>
    internal sealed class MasterGridColumnsPopup : IDisposable
    {
        readonly Button button;
        readonly VisualElement popup;
        readonly Func<IReadOnlyList<MasterGridColumn>> getColumns;
        readonly Action changed;

        public MasterGridColumnsPopup(Button button, VisualElement popup, Func<IReadOnlyList<MasterGridColumn>> getColumns, Action changed)
        {
            this.button = button;
            this.popup = popup;
            this.getColumns = getColumns;
            this.changed = changed;
            popup.style.display = DisplayStyle.None;
            button.clicked += Toggle;
            button.RegisterCallback<GeometryChangedEvent>(OnButtonGeometryChanged);
        }

        public bool IsOpen => popup.style.display.value == DisplayStyle.Flex;

        public void Dispose()
        {
            button.clicked -= Toggle;
            button.UnregisterCallback<GeometryChangedEvent>(OnButtonGeometryChanged);
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            Build();
            popup.style.display = DisplayStyle.Flex;
            button.AddToClassList("mm-debugger__button--active");
        }

        public void Close()
        {
            popup.style.display = DisplayStyle.None;
            button.RemoveFromClassList("mm-debugger__button--active");
        }

        void OnButtonGeometryChanged(GeometryChangedEvent evt)
        {
            // right aligned under the button, inside the records panel
            var parent = popup.hierarchy.parent;
            if (parent == null) return;
            var buttonBounds = button.ChangeCoordinatesTo(parent, button.contentRect);
            popup.style.top = buttonBounds.yMax + 2;
            popup.style.right = parent.layout.width - buttonBounds.xMax;
        }

        void Build()
        {
            popup.Clear();
            var columns = getColumns();

            var header = new VisualElement();
            header.AddToClassList("mm-debugger__columns-row");
            header.AddToClassList("mm-debugger__columns-row--header");
            header.Add(CreateLabel("Column", "mm-debugger__columns-name"));
            header.Add(CreateLabel("Show", "mm-debugger__columns-flag"));
            header.Add(CreateLabel("Freeze", "mm-debugger__columns-flag"));
            popup.Add(header);

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("mm-debugger__columns-list");
            foreach (var column in columns)
            {
                var c = column;
                var row = new VisualElement();
                row.AddToClassList("mm-debugger__columns-row");
                row.Add(CreateLabel(column.Title, "mm-debugger__columns-name"));

                var visible = new Toggle { value = column.Visible, tooltip = "Show this column" };
                visible.AddToClassList("mm-debugger__columns-flag");
                visible.RegisterValueChangedCallback(evt =>
                {
                    c.Visible = evt.newValue;
                    changed();
                });
                row.Add(visible);

                var frozen = new Toggle { value = column.Frozen, tooltip = "Keep this column on the left while scrolling" };
                frozen.AddToClassList("mm-debugger__columns-flag");
                frozen.RegisterValueChangedCallback(evt =>
                {
                    c.Frozen = evt.newValue;
                    changed();
                });
                row.Add(frozen);

                if (column.Locked)
                {
                    // state and primary key columns identify the row: always shown and frozen
                    visible.SetEnabled(false);
                    frozen.SetEnabled(false);
                    row.tooltip = "Always shown and frozen";
                    row.AddToClassList("mm-debugger__columns-row--locked");
                }
                list.Add(row);
            }
            popup.Add(list);

            var actions = new VisualElement();
            actions.AddToClassList("mm-debugger__columns-actions");
            actions.Add(CreateButton("Show All", "Show every column", () =>
            {
                foreach (var column in getColumns()) column.Visible = true;
                changed();
                Build();
            }));
            actions.Add(CreateButton("Reset", "Default visibility, freezing and widths", () =>
            {
                MasterGridLayout.ResetToDefaults(getColumns());
                changed();
                Build();
            }));
            var spacer = new VisualElement();
            spacer.AddToClassList("mm-debugger__spacer");
            actions.Add(spacer);
            actions.Add(CreateButton("Close", "Close (Esc)", Close));
            popup.Add(actions);
        }

        static Label CreateLabel(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }

        static Button CreateButton(string text, string tooltip, Action action)
        {
            var button = new Button(action) { text = text, tooltip = tooltip };
            button.AddToClassList("mm-debugger__button");
            return button;
        }
    }
}
