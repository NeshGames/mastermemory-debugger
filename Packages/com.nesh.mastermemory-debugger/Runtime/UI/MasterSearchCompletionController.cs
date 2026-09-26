using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Completion popup of the search box: field names, and enum / bool values after an operator; recent searches when the
    /// box is empty (or Down is pressed without a popup).
    /// Up / Down select, Tab / Enter / click accept, Esc closes the popup. Tab never moves the focus out of the search box.
    /// </summary>
    internal sealed class MasterSearchCompletionController : IDisposable
    {
        const int MaxVisibleItems = 8;
        const string Operators = "= != > >= < <= ~";
        const string ItemClass = "mm-debugger__completion-item";
        const string SelectedItemClass = "mm-debugger__completion-item--selected";
        const string InfoClass = "mm-debugger__completion-info";

        readonly TextField searchField;
        readonly VisualElement popup;
        readonly VisualElement anchor;
        readonly Func<MasterDataTypeDescriptor> typeProvider;
        readonly Func<string, string> fieldLabelProvider;
        readonly List<Label> items = new List<Label>();

        MasterRecordQueryCompletion.Context context;
        // the candidates are recent searches that replace the whole text
        bool showingHistory;
        int selected;
        int firstVisible;
        // closed with Esc: stays closed until the text changes
        string dismissedText;

        public MasterSearchCompletionController(TextField searchField, VisualElement popup, VisualElement anchor, Func<MasterDataTypeDescriptor> typeProvider,
            Func<string, string> fieldLabelProvider = null)
        {
            this.fieldLabelProvider = fieldLabelProvider;
            this.searchField = searchField;
            this.popup = popup;
            this.anchor = anchor;
            this.typeProvider = typeProvider;
            popup.style.display = DisplayStyle.None;

            searchField.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            searchField.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            searchField.RegisterCallback<KeyUpEvent>(OnKeyUp);
            searchField.RegisterCallback<PointerUpEvent>(OnPointerUp);
            searchField.RegisterCallback<FocusInEvent>(OnFocusIn);
            searchField.RegisterCallback<FocusOutEvent>(OnFocusOut);
            anchor.RegisterCallback<GeometryChangedEvent>(OnAnchorGeometryChanged);
        }

        /// <summary>True while candidates are shown (Esc then closes the popup instead of the debugger).</summary>
        public bool IsOpen => popup.style.display.value == DisplayStyle.Flex;

        public void Dispose()
        {
            searchField.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            searchField.UnregisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            searchField.UnregisterCallback<KeyUpEvent>(OnKeyUp);
            searchField.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            searchField.UnregisterCallback<FocusInEvent>(OnFocusIn);
            searchField.UnregisterCallback<FocusOutEvent>(OnFocusOut);
            anchor.UnregisterCallback<GeometryChangedEvent>(OnAnchorGeometryChanged);
        }

        /// <summary>Called when the table changes: the field names change with it.</summary>
        public void Refresh()
        {
            if (IsFocused()) UpdatePopup();
            else Close();
        }

        public void Close()
        {
            dismissedText = searchField.value;
            Hide();
        }

        // ------------------------------------------------------------------ events

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.ctrlKey || evt.altKey || evt.commandKey) return;
            var canAccept = IsOpen && HasChoices;
            switch (evt.keyCode)
            {
                case KeyCode.Tab:
                    if (canAccept) Accept(selected);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (!canAccept) return;
                    Accept(selected);
                    break;
                case KeyCode.DownArrow:
                    if (!canAccept)
                    {
                        if (!ShowHistory(searchField.value)) return;
                        break;
                    }
                    Select(selected + 1);
                    break;
                case KeyCode.UpArrow:
                    if (!canAccept) return;
                    Select(selected - 1);
                    break;
                case KeyCode.None:
                    // Linux sends the tab character separately
                    if (evt.character != '\t') return;
                    break;
                default:
                    return;
            }
            evt.StopPropagation();
            searchField.focusController?.IgnoreEvent(evt);
        }

        void OnNavigationMove(NavigationMoveEvent evt)
        {
            var isTab = evt.direction == NavigationMoveEvent.Direction.Next || evt.direction == NavigationMoveEvent.Direction.Previous;
            var isArrow = evt.direction == NavigationMoveEvent.Direction.Up || evt.direction == NavigationMoveEvent.Direction.Down;
            if (!isTab && !(isArrow && IsOpen)) return;
            evt.StopPropagation();
            searchField.focusController?.IgnoreEvent(evt);
        }

        void OnKeyUp(KeyUpEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Tab:
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Escape:
                    return;
                default:
                    UpdatePopup();
                    return;
            }
        }

        void OnPointerUp(PointerUpEvent evt) => UpdatePopup();

        void OnFocusIn(FocusInEvent evt) => searchField.schedule.Execute(UpdatePopup);

        void OnFocusOut(FocusOutEvent evt) => Hide();

        void OnAnchorGeometryChanged(GeometryChangedEvent evt)
        {
            var bounds = anchor.layout;
            popup.style.top = bounds.yMax;
            popup.style.left = bounds.xMin;
        }

        void OnItemPointerDown(PointerDownEvent evt)
        {
            // keep the focus in the search box
            evt.StopPropagation();
            searchField.focusController?.IgnoreEvent(evt);
            if ((evt.currentTarget as VisualElement)?.userData is int index) Accept(index);
        }

        // ------------------------------------------------------------------ popup

        bool HasChoices => context != null && (showingHistory || !IsComplete(context));

        void UpdatePopup()
        {
            var text = searchField.value ?? string.Empty;
            if (text == dismissedText)
            {
                Hide();
                return;
            }
            dismissedText = null;

            if (text.Length == 0)
            {
                if (!ShowHistory(text)) Hide();
                return;
            }
            showingHistory = false;

            var previous = context != null && selected < context.Candidates.Count ? context.Candidates[selected] : null;
            context = MasterRecordQueryCompletion.GetContext(text, searchField.cursorIndex, typeProvider());
            // do not pop up on an empty field position (after a space); values pop up right after the operator
            if (context == null || (context.Word.Length == 0 && !context.IsValue))
            {
                context = null;
                Hide();
                return;
            }

            selected = Math.Max(0, previous != null ? context.Candidates.IndexOf(previous) : 0);
            firstVisible = 0;
            Render();
            popup.style.display = DisplayStyle.Flex;
        }

        /// <summary>Lists the recent searches that contain <paramref name="text"/>; false when there is none.</summary>
        bool ShowHistory(string text)
        {
            var entries = MasterSearchHistory.Find(text);
            if (entries.Count == 0)
            {
                context = null;
                showingHistory = false;
                return false;
            }
            text ??= string.Empty;
            context = new MasterRecordQueryCompletion.Context { Start = 0, End = text.Length, Word = text, Candidates = entries };
            showingHistory = true;
            selected = 0;
            firstVisible = 0;
            Render();
            popup.style.display = DisplayStyle.Flex;
            return true;
        }

        void Select(int index)
        {
            var count = context.Candidates.Count;
            selected = (index % count + count) % count;
            Render();
        }

        void Accept(int index)
        {
            if (context == null || index < 0 || index >= context.Candidates.Count) return;
            var text = MasterRecordQueryCompletion.Apply(searchField.value, context, context.Candidates[index], out var caret);
            searchField.value = text;
            SetCaret(caret);
            // the text field may move the caret while it applies the new value
            searchField.schedule.Execute(() => SetCaret(caret));
            context = null;
            showingHistory = false;
            Close();
        }

        void Render()
        {
            popup.Clear();
            items.Clear();

            if (!showingHistory && IsComplete(context))
            {
                // the field name is complete: show what can follow it
                popup.Add(CreateInfo($"{context.Candidates[0]}  {Operators}"));
                return;
            }

            var count = context.Candidates.Count;
            if (selected < firstVisible) firstVisible = selected;
            if (selected >= firstVisible + MaxVisibleItems) firstVisible = selected - MaxVisibleItems + 1;

            if (showingHistory) popup.Add(CreateInfo("Recent searches"));
            else if (context.IsValue) popup.Add(CreateInfo(context.ValueField.Name));
            if (firstVisible > 0) popup.Add(CreateInfo($"▲ {firstVisible} more"));
            var last = Math.Min(count, firstVisible + MaxVisibleItems);
            for (var i = firstVisible; i < last; i++)
            {
                var candidate = context.Candidates[i];
                // field names: the label of the selected language next to the code name that is inserted
                var label = context.IsValue || showingHistory ? null : fieldLabelProvider?.Invoke(candidate);
                var item = new Label(label != null && label != candidate ? $"{candidate}    {label}" : candidate) { userData = i };
                item.AddToClassList(ItemClass);
                item.EnableInClassList(SelectedItemClass, i == selected);
                item.RegisterCallback<PointerDownEvent>(OnItemPointerDown);
                items.Add(item);
                popup.Add(item);
            }
            if (last < count) popup.Add(CreateInfo($"▼ {count - last} more"));
        }

        static Label CreateInfo(string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(InfoClass);
            return label;
        }

        static bool IsComplete(MasterRecordQueryCompletion.Context context)
        {
            return !context.IsValue && context.Candidates.Count == 1 && string.Equals(context.Candidates[0], context.Word, StringComparison.Ordinal);
        }

        void Hide() => popup.style.display = DisplayStyle.None;

        bool IsFocused()
        {
            var focused = searchField.focusController?.focusedElement as VisualElement;
            return focused != null && (focused == searchField || searchField.Contains(focused));
        }

        void SetCaret(int caret)
        {
            var length = searchField.value?.Length ?? 0;
            caret = Mathf.Clamp(caret, 0, length);
            searchField.SelectRange(caret, caret);
        }
    }
}
