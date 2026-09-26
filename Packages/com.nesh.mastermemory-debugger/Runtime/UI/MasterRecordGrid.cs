using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Virtualized record grid: a header and a ListView whose rows are split into a frozen part (always visible) and a part
    /// that scrolls horizontally with the header. Column widths can be dragged, a header click sorts.
    /// Columns and rows are always replaced together, so a row is never bound to the columns of another table.
    /// </summary>
    internal sealed class MasterRecordGrid : IDisposable
    {
        public const float RowHeight = 22f;
        const float WheelStep = 40f;
        const string CellClass = "mm-debugger__cell";

        sealed class RowCells
        {
            public int Generation;
            public VisualElement Frozen;
            public VisualElement Content;
            public Label[] Cells;
        }

        sealed class HeaderCell
        {
            public MasterGridColumn Column;
            public VisualElement Root;
            public Label Sort;
        }

        readonly VisualElement root;
        readonly VisualElement headerFrozen;
        readonly VisualElement headerViewport;
        readonly VisualElement headerContent;
        readonly ListView listView;
        readonly Scroller horizontalScroller;
        readonly List<RowCells> rows = new List<RowCells>();
        readonly List<HeaderCell> headerCells = new List<HeaderCell>();
        readonly List<MasterGridColumn> frozen = new List<MasterGridColumn>();
        readonly List<MasterGridColumn> scrolled = new List<MasterGridColumn>();

        IReadOnlyList<MasterGridColumn> columns = Array.Empty<MasterGridColumn>();
        IList items = new List<object>();
        int generation;
        float scrollX;

        // column resize
        HeaderCell resizing;
        float resizeStartX;
        float resizeStartWidth;

        public MasterRecordGrid(VisualElement host)
        {
            root = host;
            root.AddToClassList("mm-debugger__grid");

            var header = new VisualElement();
            header.AddToClassList("mm-debugger__grid-header");
            headerFrozen = new VisualElement();
            headerFrozen.AddToClassList("mm-debugger__grid-frozen");
            headerViewport = new VisualElement();
            headerViewport.AddToClassList("mm-debugger__grid-viewport");
            headerContent = new VisualElement();
            headerContent.AddToClassList("mm-debugger__grid-content");
            headerViewport.Add(headerContent);
            header.Add(headerFrozen);
            header.Add(headerViewport);
            root.Add(header);

            listView = new ListView
            {
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                horizontalScrollingEnabled = false,
                makeItem = MakeRow,
                bindItem = BindRow,
                itemsSource = items,
            };
            listView.AddToClassList("mm-debugger__grid-rows");
            listView.selectionChanged += OnSelectionChanged;
            root.Add(listView);

            horizontalScroller = new Scroller(0f, 0f, OnHorizontalScroll, SliderDirection.Horizontal);
            horizontalScroller.AddToClassList("mm-debugger__grid-hscroll");
            root.Add(horizontalScroller);

            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            root.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
        }

        /// <summary>Index of the item selected by the user (keyboard or pointer).</summary>
        public event Action<int> ItemSelected;

        /// <summary>A header was clicked; <see cref="SortKey"/> / <see cref="SortDescending"/> already changed.</summary>
        public event Action SortChanged;

        /// <summary>A column was resized by dragging its header edge.</summary>
        public event Action ColumnResized;

        public string SortKey { get; private set; }

        public bool SortDescending { get; private set; }

        public VisualElement Root => root;

        public void Dispose()
        {
            listView.selectionChanged -= OnSelectionChanged;
            root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            root.UnregisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
        }

        /// <summary>Replaces the columns and the rows at once (table switch).</summary>
        public void SetContent(IReadOnlyList<MasterGridColumn> newColumns, IList newItems)
        {
            columns = newColumns ?? Array.Empty<MasterGridColumn>();
            items = newItems ?? new List<object>();
            if (SortKey != null && !columns.Any(x => x.Key == SortKey)) SortKey = null;
            scrollX = 0f;
            Relayout();
        }

        /// <summary>Rebuilds header and rows after visibility / freezing changed.</summary>
        public void Relayout()
        {
            MasterGridLayout.Split(columns, frozen, scrolled);
            generation++;
            BuildHeader();
            rows.Clear();
            listView.itemsSource = items;
            listView.Rebuild();
            UpdateHorizontalScroll();
        }

        /// <summary>Same columns, new or changed items.</summary>
        public void RefreshItems()
        {
            listView.itemsSource = items;
            listView.RefreshItems();
        }

        public void ClearSort()
        {
            SortKey = null;
            UpdateSortIndicators();
        }

        public void SetSelectionWithoutNotify(int index)
        {
            if (index >= 0) listView.SetSelectionWithoutNotify(new[] { index });
            else listView.ClearSelection();
        }

        public void ScrollToItem(int index) => listView.ScrollToItem(index);

        // ------------------------------------------------------------------ header

        void BuildHeader()
        {
            headerFrozen.Clear();
            headerContent.Clear();
            headerCells.Clear();
            foreach (var column in frozen) headerFrozen.Add(CreateHeaderCell(column));
            foreach (var column in scrolled) headerContent.Add(CreateHeaderCell(column));
            headerFrozen.EnableInClassList("mm-debugger__grid-frozen--empty", frozen.Count == 0);
            ApplyWidths();
            UpdateSortIndicators();
        }

        VisualElement CreateHeaderCell(MasterGridColumn column)
        {
            var cell = new HeaderCell { Column = column, Root = new VisualElement() };
            cell.Root.AddToClassList("mm-debugger__grid-header-cell");
            cell.Root.tooltip = column.Tooltip ?? column.Title;

            var title = new Label(column.Title);
            title.AddToClassList("mm-debugger__grid-header-title");
            cell.Root.Add(title);
            cell.Sort = new Label();
            cell.Sort.AddToClassList("mm-debugger__grid-sort");
            cell.Root.Add(cell.Sort);
            cell.Root.RegisterCallback<ClickEvent>(_ => OnHeaderClicked(column));

            var handle = new VisualElement();
            handle.AddToClassList("mm-debugger__grid-resize");
            handle.RegisterCallback<PointerDownEvent>(evt => OnResizeStart(cell, handle, evt));
            handle.RegisterCallback<PointerMoveEvent>(evt => OnResizeMove(handle, evt));
            handle.RegisterCallback<PointerUpEvent>(evt => OnResizeEnd(handle, evt));
            // a click on the handle must not sort
            handle.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            cell.Root.Add(handle);

            headerCells.Add(cell);
            return cell.Root;
        }

        void OnHeaderClicked(MasterGridColumn column)
        {
            // ascending → descending → not sorted
            if (SortKey != column.Key)
            {
                SortKey = column.Key;
                SortDescending = false;
            }
            else if (!SortDescending)
            {
                SortDescending = true;
            }
            else
            {
                SortKey = null;
            }
            UpdateSortIndicators();
            SortChanged?.Invoke();
        }

        void UpdateSortIndicators()
        {
            foreach (var cell in headerCells)
            {
                var sorted = cell.Column.Key == SortKey;
                cell.Sort.text = sorted ? (SortDescending ? "▼" : "▲") : string.Empty;
                cell.Root.EnableInClassList("mm-debugger__grid-header-cell--sorted", sorted);
            }
        }

        void OnResizeStart(HeaderCell cell, VisualElement handle, PointerDownEvent evt)
        {
            resizing = cell;
            resizeStartX = evt.position.x;
            resizeStartWidth = cell.Column.Width;
            handle.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnResizeMove(VisualElement handle, PointerMoveEvent evt)
        {
            if (resizing == null || !handle.HasPointerCapture(evt.pointerId)) return;
            resizing.Column.Width = Mathf.Max(MasterGridColumn.MinWidth, resizeStartWidth + evt.position.x - resizeStartX);
            ApplyWidths();
            UpdateHorizontalScroll();
            evt.StopPropagation();
        }

        void OnResizeEnd(VisualElement handle, PointerUpEvent evt)
        {
            if (resizing == null) return;
            handle.ReleasePointer(evt.pointerId);
            resizing = null;
            evt.StopPropagation();
            ColumnResized?.Invoke();
        }

        // ------------------------------------------------------------------ rows

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("mm-debugger__grid-row");
            var cells = new RowCells
            {
                Generation = generation,
                Frozen = new VisualElement(),
                Content = new VisualElement(),
                Cells = new Label[frozen.Count + scrolled.Count],
            };
            cells.Frozen.AddToClassList("mm-debugger__grid-frozen");
            cells.Frozen.EnableInClassList("mm-debugger__grid-frozen--empty", frozen.Count == 0);
            var viewport = new VisualElement();
            viewport.AddToClassList("mm-debugger__grid-viewport");
            cells.Content.AddToClassList("mm-debugger__grid-content");
            viewport.Add(cells.Content);
            row.Add(cells.Frozen);
            row.Add(viewport);

            var i = 0;
            foreach (var column in frozen) cells.Frozen.Add(cells.Cells[i++] = CreateCell(column));
            foreach (var column in scrolled) cells.Content.Add(cells.Cells[i++] = CreateCell(column));

            row.userData = cells;
            rows.Add(cells);
            ApplyWidths(cells);
            cells.Content.style.left = -scrollX;
            return row;
        }

        static Label CreateCell(MasterGridColumn column)
        {
            var label = new Label();
            label.AddToClassList(CellClass);
            if (column.CellClass != null)
            {
                foreach (var name in column.CellClass.Split(' ')) label.AddToClassList(name);
            }
            return label;
        }

        void BindRow(VisualElement element, int index)
        {
            if (!(element.userData is RowCells cells) || cells.Generation != generation) return;
            if (index < 0 || index >= items.Count || !(items[index] is MasterMemoryRecordDescriptor record)) return;
            var i = 0;
            foreach (var column in frozen) column.Bind(cells.Cells[i++], record);
            foreach (var column in scrolled) column.Bind(cells.Cells[i++], record);
        }

        void OnSelectionChanged(IEnumerable<object> selection)
        {
            var index = listView.selectedIndex;
            if (index >= 0) ItemSelected?.Invoke(index);
        }

        // ------------------------------------------------------------------ widths and horizontal scroll

        void ApplyWidths()
        {
            foreach (var cell in headerCells) cell.Root.style.width = cell.Column.Width;
            headerFrozen.style.width = MasterGridLayout.TotalWidth(frozen);
            headerContent.style.width = MasterGridLayout.TotalWidth(scrolled);
            foreach (var row in rows)
            {
                if (row.Generation == generation) ApplyWidths(row);
            }
        }

        void ApplyWidths(RowCells row)
        {
            var i = 0;
            foreach (var column in frozen) row.Cells[i++].style.width = column.Width;
            foreach (var column in scrolled) row.Cells[i++].style.width = column.Width;
            row.Frozen.style.width = MasterGridLayout.TotalWidth(frozen);
            row.Content.style.width = MasterGridLayout.TotalWidth(scrolled);
        }

        void OnGeometryChanged(GeometryChangedEvent evt) => UpdateHorizontalScroll();

        void UpdateHorizontalScroll()
        {
            var contentWidth = MasterGridLayout.TotalWidth(scrolled);
            var verticalScroller = listView.Q<ScrollView>()?.verticalScroller;
            var scrollbarWidth = verticalScroller != null && verticalScroller.resolvedStyle.display == DisplayStyle.Flex ? verticalScroller.layout.width : 0f;
            var viewportWidth = listView.layout.width - MasterGridLayout.TotalWidth(frozen) - (float.IsNaN(scrollbarWidth) ? 0f : scrollbarWidth);
            if (float.IsNaN(viewportWidth)) return;

            var max = MasterGridLayout.MaxScroll(contentWidth, viewportWidth);
            horizontalScroller.style.display = max > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            horizontalScroller.highValue = max;
            if (contentWidth > 0f) horizontalScroller.Adjust(Mathf.Clamp01(viewportWidth / contentWidth));
            SetScroll(Mathf.Min(scrollX, max));
        }

        void OnHorizontalScroll(float value) => SetScroll(value);

        void SetScroll(float value)
        {
            scrollX = Mathf.Max(0f, value);
            if (!Mathf.Approximately(horizontalScroller.value, scrollX)) horizontalScroller.value = scrollX;
            headerContent.style.left = -scrollX;
            foreach (var row in rows) row.Content.style.left = -scrollX;
        }

        void OnWheel(WheelEvent evt)
        {
            // Shift + wheel, or a horizontal wheel / touchpad, scrolls the columns
            var horizontal = Mathf.Abs(evt.delta.x) > Mathf.Abs(evt.delta.y) ? evt.delta.x : (evt.shiftKey ? evt.delta.y : 0f);
            if (horizontal == 0f || horizontalScroller.highValue <= 0f) return;
            horizontalScroller.value = Mathf.Clamp(horizontalScroller.value + Mathf.Sign(horizontal) * WheelStep, 0f, horizontalScroller.highValue);
            evt.StopPropagation();
        }
    }
}
