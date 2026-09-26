using System;
using System.Collections.Generic;
namespace UnityEngine.UIElements
{
    public interface IPanel { VisualElement visualTree { get; } }
    public class PopupField<T> : BasePopupField<T, T>
    {
        public PopupField() : base(null) { }
        public virtual List<T> choices { get; set; } = new List<T>();
        public int index { get; set; }
    }
    public class DropdownField : PopupField<string> { public DropdownField() { } public DropdownField(string label) { } public DropdownField(string label, List<string> choices, int defaultIndex, Func<string, string> formatSelectedValueCallback = null, Func<string, string> formatListItemCallback = null) { } }
    public struct TreeViewItemData<T> { public TreeViewItemData(int id, T data, List<TreeViewItemData<T>> children = null) { } }
    public abstract class BaseTreeView : BaseVerticalCollectionView
    {
        public void SetRootItems<T>(IList<TreeViewItemData<T>> rootItems) { }
        public T GetItemDataForIndex<T>(int index) => default;
        public void SetSelectionById(int id) { }
        public void SetSelectionByIdWithoutNotify(IEnumerable<int> ids) { }
        public bool IsExpanded(int id) => true;
        public void CollapseItem(int id, bool collapseAllChildren = false, bool refresh = true) { }
        public void ExpandItem(int id, bool expandAllChildren = false, bool refresh = true) { }
        public void ExpandAll() { }
    }
    public class TreeView : BaseTreeView { public Func<VisualElement> makeItem { get; set; } public Action<VisualElement, int> bindItem { get; set; } }
}
namespace UnityEngine.UIElements
{
    public struct Length { public static implicit operator Length(float v) => default; }
    public enum ColumnSortingMode { None, Default, Custom }
    public enum SortDirection { Ascending, Descending }
    public class SortColumnDescription { public string columnName { get; set; } public int columnIndex { get; set; } public SortDirection direction { get; set; } }
    public class SortColumnDescriptions : System.Collections.Generic.List<SortColumnDescription> { }
    public class Column
    {
        public bool resizable { get; set; }
        public string name { get; set; }
        public string title { get; set; }
        public Length width { get; set; }
        public Length minWidth { get; set; }
        public bool sortable { get; set; }
        public bool stretchable { get; set; }
        public System.Func<VisualElement> makeCell { get; set; }
        public System.Action<VisualElement, int> bindCell { get; set; }
    }
    public class Columns : System.Collections.Generic.List<Column> { }
    public class MultiColumnListView : BaseListView
    {
        public Columns columns { get; } = new Columns();
        public ColumnSortingMode sortingMode { get; set; }
        public event System.Action columnSortingChanged;
        public System.Collections.Generic.IEnumerable<SortColumnDescription> sortedColumns => new SortColumnDescription[0];
        public SortColumnDescriptions sortColumnDescriptions { get; } = new SortColumnDescriptions();
    }
}
namespace UnityEngine.UIElements
{
    public enum TrickleDown { NoTrickleDown, TrickleDown }
    public class EventBase { public object currentTarget { get; } public object target { get; } public void StopPropagation() { } }
    public class KeyboardEventStub : EventBase { public bool actionKey { get; } public KeyCode keyCode { get; } public char character { get; } public bool shiftKey { get; } public bool ctrlKey { get; } public bool altKey { get; } public bool commandKey { get; } }
    public class KeyDownEvent : KeyboardEventStub { }
    public class KeyUpEvent : KeyboardEventStub { }
    public class PointerUpEvent : EventBase { public int pointerId => 0; }
    public class PointerDownEvent : EventBase { public UnityEngine.Vector3 position => default; public int pointerId => 0; }
    public class PointerMoveEvent : EventBase { public UnityEngine.Vector3 position => default; public int pointerId => 0; }
    public class WheelEvent : EventBase { public UnityEngine.Vector3 delta => default; public bool shiftKey => false; }
    public class ClickEvent : EventBase { public int clickCount => 1; }
    public class FocusInEvent : EventBase { }
    public class FocusOutEvent : EventBase { }
    public class GeometryChangedEvent : EventBase { public UnityEngine.Rect newRect => default; }
    public class NavigationMoveEvent : EventBase { public enum Direction { None, Left, Up, Right, Down, Next, Previous } public Direction direction { get; } }
    public class Foldout : BindableElementStub, INotifyValueChanged<bool>
    {
        public string text { get; set; }
        public bool value { get; set; }
        public void SetValueWithoutNotify(bool v) { }
    }
    public class BindableElementStub : VisualElement { }
    public static class VisualElementEventExtensions { }
}
namespace UnityEngine
{
    public class GUIUtility { public static string systemCopyBuffer { get; set; } }
}
namespace AOT
{
    [System.AttributeUsage(System.AttributeTargets.Method)] public class MonoPInvokeCallbackAttribute : System.Attribute { public MonoPInvokeCallbackAttribute(System.Type type) { } }
}
namespace UnityEngine.UIElements
{
    public enum TwoPaneSplitViewOrientation { Horizontal, Vertical }
    public class TwoPaneSplitView : VisualElement { public float fixedPaneInitialDimension { get; set; } public TwoPaneSplitView() { } public TwoPaneSplitView(int fixedPaneIndex, float fixedPaneStartDimension, TwoPaneSplitViewOrientation orientation) { } }
}

namespace UnityEngine.UIElements
{
    public struct FontDefinition { public static FontDefinition FromFont(UnityEngine.Font f) => default; }
    public struct StyleFontDefinition { public StyleFontDefinition(FontDefinition f) { } }
}
