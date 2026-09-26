using System;
using System.Collections;
using System.Collections.Generic;
namespace UnityEngine.UIElements
{
    public enum DisplayStyle { Flex, None }
    public enum SelectionType { None, Single, Multiple }
    public enum AlternatingRowBackground { None, ContentOnly, All }
    public enum PanelScaleMode { ConstantPixelSize, ConstantPhysicalSize, ScaleWithScreenSize }
    public enum PanelScreenMatchMode { MatchWidthOrHeight, Shrink, Expand }
    public enum PickingMode { Position, Ignore }
    public struct StyleEnum<T> where T : struct, IConvertible { public T value { get; set; } public StyleEnum(T v) { value = v; } public static implicit operator StyleEnum<T>(T v) => new StyleEnum<T>(v); }
    public struct StyleColor { public Color value { get; set; } public StyleColor(Color c) { value = c; } public static implicit operator StyleColor(Color c) => new StyleColor(c); }
    public interface IStyle { StyleEnum<DisplayStyle> display { get; set; } StyleColor backgroundColor { get; set; } StyleLength paddingLeft { get; set; } StyleLength paddingRight { get; set; } StyleLength paddingTop { get; set; } StyleLength fontSize { get; set; } StyleEnum<FontStyle> unityFontStyleAndWeight { get; set; } StyleLength marginBottom { get; set; } StyleLength marginTop { get; set; } StyleEnum<FlexDirection> flexDirection { get; set; } StyleFloat flexGrow { get; set; } StyleEnum<TextAnchor> unityTextAlign { get; set; } StyleLength top { get; set; } StyleLength left { get; set; } StyleLength width { get; set; } StyleLength height { get; set; } StyleLength right { get; set; } StyleFontDefinition unityFontDefinition { get; set; } }
    class StyleImpl : IStyle { public StyleEnum<DisplayStyle> display { get; set; } public StyleColor backgroundColor { get; set; } public StyleLength paddingLeft { get; set; } public StyleLength paddingRight { get; set; } public StyleLength paddingTop { get; set; } public StyleLength fontSize { get; set; } public StyleEnum<FontStyle> unityFontStyleAndWeight { get; set; } public StyleLength marginBottom { get; set; } public StyleLength marginTop { get; set; } public StyleEnum<FlexDirection> flexDirection { get; set; } public StyleFloat flexGrow { get; set; } public StyleEnum<TextAnchor> unityTextAlign { get; set; } public StyleLength top { get; set; } public StyleLength left { get; set; } public StyleLength width { get; set; } public StyleLength height { get; set; } public StyleLength right { get; set; } public StyleFontDefinition unityFontDefinition { get; set; } }
    public class StyleSheet : ScriptableObject { }
    public class ThemeStyleSheet : StyleSheet { }
    public class VisualTreeAsset : ScriptableObject { public TemplateContainer CloneTree() => null; }
    public class TemplateContainer : VisualElement { }
    public class VisualElementStyleSheetSet { public void Add(StyleSheet s) { } }
    public delegate void EventCallback<in TEventType>(TEventType evt);
    public class ChangeEvent<T> : EventBase { public T previousValue { get; } public T newValue { get; } }
    public interface INotifyValueChanged<T> { T value { get; set; } void SetValueWithoutNotify(T newValue); }
    public static class INotifyValueChangedExtensions
    {
        public static bool RegisterValueChangedCallback<T>(this INotifyValueChanged<T> control, EventCallback<ChangeEvent<T>> callback) => true;
        public static bool UnregisterValueChangedCallback<T>(this INotifyValueChanged<T> control, EventCallback<ChangeEvent<T>> callback) => true;
    }
    public interface IVisualElementScheduledItem { void Pause(); void Resume(); void ExecuteLater(long delayMs); IVisualElementScheduledItem StartingIn(long delayMs); }
    public interface IVisualElementScheduler { IVisualElementScheduledItem Execute(Action updateEvent); }
    class Sched : IVisualElementScheduler, IVisualElementScheduledItem { public IVisualElementScheduledItem Execute(Action a) => this; public void Pause() { } public void Resume() { } public void ExecuteLater(long d) { } public IVisualElementScheduledItem StartingIn(long d) => this; }
    public class VisualElement : Focusable
    {
        public string name { get; set; }
        public string tooltip { get; set; }
        public IPanel panel => null;
        public PickingMode pickingMode { get; set; }
        public UnityEngine.Rect layout => default;
        public object userData { get; set; }
        public int childCount => 0;
        public VisualElement contentContainer => this;
        public bool ClassListContains(string c) => false;
        public void RemoveFromHierarchy() { }
        public FocusController focusController => null;
        public IResolvedStyle resolvedStyle => new IResolvedStyle();
        public VisualElementHierarchy hierarchy => new VisualElementHierarchy();
        public UnityEngine.Rect contentRect => default;
        public UnityEngine.Rect ChangeCoordinatesTo(VisualElement dest, UnityEngine.Rect rect) => rect;
        public void CapturePointer(int pointerId) { }
        public void ReleasePointer(int pointerId) { }
        public bool HasPointerCapture(int pointerId) => false;
        public IStyle style { get; } = new StyleImpl();
        public VisualElementStyleSheetSet styleSheets { get; } = new VisualElementStyleSheetSet();
        public IVisualElementScheduler schedule { get; } = new Sched();
        public void Add(VisualElement child) { }
        public void Clear() { }
        public void AddToClassList(string c) { }
        public void RemoveFromClassList(string c) { }
        public void EnableInClassList(string c, bool enable) { }
        public void SetEnabled(bool value) { }
        public void RegisterCallback<T>(EventCallback<T> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) { }
        public void UnregisterCallback<T>(EventCallback<T> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown) { }
        public bool Contains(VisualElement child) => false;
        public T GetFirstAncestorOfType<T>() where T : class => null;
    }
    public static class UQueryExtensions
    {
        public static T Q<T>(this VisualElement e, string name = null, string className = null) where T : VisualElement => null;
        public static VisualElement Q(this VisualElement e, string name = null, string className = null) => null;
    }
    public interface ITextSelection { bool isSelectable { get; set; } }
    public interface ITextEdition { string placeholder { get; set; } bool isReadOnly { get; set; } }
    public class TextElement : VisualElement { public string text { get; set; } public bool enableRichText { get; set; } public ITextSelection selection => null; }
    public class Label : TextElement { public Label() { } public Label(string text) { this.text = text; } }
    public class Button : TextElement { public Button() { } public Button(Action clickEvent) { } public event Action clicked; }
    public abstract class BaseField<TValueType> : VisualElement, INotifyValueChanged<TValueType>
    {
        protected BaseField(string label) { }
        public virtual TValueType value { get; set; }
        public virtual void SetValueWithoutNotify(TValueType newValue) { }
        public string label { get; set; }
    }
    public abstract class TextInputBaseField<T> : BaseField<T>
    {
        protected TextInputBaseField(string label) : base(label) { }
        public bool isDelayed { get; set; }
        public bool isReadOnly { get; set; }
        public ITextEdition textEdition => null;
        public int cursorIndex { get; set; }
        public void SelectRange(int cursorIndex, int selectionIndex) { }
    }
    public class FocusController { public Focusable focusedElement => null; public void IgnoreEvent(EventBase evt) { } }
    public class Focusable { }
    public abstract class TextValueField<T> : TextInputBaseField<T> { protected TextValueField(string label) : base(label) { } }
    public class TextField : TextInputBaseField<string> { public TextField() : base(null) { } public TextField(string label) : base(label) { } public bool multiline { get; set; } }
    public class IntegerField : TextValueField<int> { public IntegerField() : base(null) { } public IntegerField(string label, int maxLength = 1000) : base(label) { } }
    public class UnsignedIntegerField : TextValueField<uint> { public UnsignedIntegerField() : base(null) { } public UnsignedIntegerField(string label, int maxLength = 1000) : base(label) { } }
    public class LongField : TextValueField<long> { public LongField() : base(null) { } public LongField(string label, int maxLength = 1000) : base(label) { } }
    public class UnsignedLongField : TextValueField<ulong> { public UnsignedLongField() : base(null) { } public UnsignedLongField(string label, int maxLength = 1000) : base(label) { } }
    public class FloatField : TextValueField<float> { public FloatField() : base(null) { } public FloatField(string label, int maxLength = 1000) : base(label) { } }
    public class DoubleField : TextValueField<double> { public DoubleField() : base(null) { } public DoubleField(string label, int maxLength = 1000) : base(label) { } }
    public class BaseBoolField : BaseField<bool> { public BaseBoolField(string label) : base(label) { } }
    public class Toggle : BaseBoolField { public Toggle() : base(null) { } public Toggle(string label) : base(label) { } }
    public class BasePopupField<T, U> : BaseField<T> { public BasePopupField(string label) : base(label) { } }
    public class EnumField : BasePopupField<Enum, Enum> { public EnumField() : base(null) { } public EnumField(Enum defaultValue) : base(null) { } public EnumField(string label, Enum defaultValue = null) : base(label) { } }
    public abstract class BaseCompositeField<T, F, V> : BaseField<T> { protected BaseCompositeField(string label, int n) : base(label) { } }
    public class Vector2Field : BaseCompositeField<Vector2, FloatField, float> { public Vector2Field() : base(null, 2) { } public Vector2Field(string label) : base(label, 2) { } }
    public class Vector3Field : BaseCompositeField<Vector3, FloatField, float> { public Vector3Field() : base(null, 3) { } public Vector3Field(string label) : base(label, 3) { } }
    public class Vector2IntField : BaseCompositeField<Vector2Int, IntegerField, int> { public Vector2IntField() : base(null, 2) { } public Vector2IntField(string label) : base(label, 2) { } }
    public class Vector3IntField : BaseCompositeField<Vector3Int, IntegerField, int> { public Vector3IntField() : base(null, 3) { } public Vector3IntField(string label) : base(label, 3) { } }
    public class ScrollView : VisualElement { public ScrollView() { } public ScrollView(ScrollViewMode mode) { } public void ScrollTo(VisualElement child) { } public Scroller verticalScroller => null; }
    public enum ScrollViewMode { Vertical, Horizontal, VerticalAndHorizontal }
    public enum SliderDirection { Horizontal, Vertical }
    public class Scroller : VisualElement { public Scroller(float low, float high, Action<float> changed, SliderDirection direction) { } public float value { get; set; } public float highValue { get; set; } public void Adjust(float factor) { } }
    public enum CollectionVirtualizationMethod { FixedHeight, DynamicHeight }
    public class IResolvedStyle { public DisplayStyle display => default; }
    public class VisualElementHierarchy { public VisualElement parent => null; }
    public abstract class BaseVerticalCollectionView : VisualElement
    {
        public IList itemsSource { get; set; }
        public float fixedItemHeight { get; set; }
        public SelectionType selectionType { get; set; }
        public AlternatingRowBackground showAlternatingRowBackgrounds { get; set; }
        public event Action<IEnumerable<object>> selectionChanged;
        public int selectedIndex { get; set; }
        public CollectionVirtualizationMethod virtualizationMethod { get; set; }
        public bool horizontalScrollingEnabled { get; set; }
        public void RefreshItems() { }
        public void Rebuild() { }
        public void ScrollToItem(int index) { }
        public void SetSelection(int index) { }
        public void SetSelectionWithoutNotify(IEnumerable<int> indices) { }
        public void ClearSelection() { }
    }
    public abstract class BaseListView : BaseVerticalCollectionView { }
    public class ListView : BaseListView { public Func<VisualElement> makeItem { get; set; } public Action<VisualElement, int> bindItem { get; set; } }
    public class PanelSettings : ScriptableObject
    {
        public ThemeStyleSheet themeStyleSheet { get; set; }
        public PanelScaleMode scaleMode { get; set; }
        public Vector2Int referenceResolution { get; set; }
        public PanelScreenMatchMode screenMatchMode { get; set; }
        public float match { get; set; }
        public float scale { get; set; }
        public float sortingOrder { get; set; }
        public bool clearColor { get; set; }
    }
    public sealed class UIDocument : MonoBehaviour
    {
        public PanelSettings panelSettings { get; set; }
        public VisualTreeAsset visualTreeAsset { get; set; }
        public VisualElement rootVisualElement => null;
        public float sortingOrder { get; set; }
    }
}
