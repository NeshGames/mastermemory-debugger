using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Tab completion in the search box and the candidate hint shown under it.
    /// Tab never moves the focus out of the search box.
    /// </summary>
    internal sealed class MasterSearchCompletionController : IDisposable
    {
        const int MaxHintItems = 12;
        const string Operators = "= != > >= < <= ~";

        readonly TextField searchField;
        readonly Label hint;
        readonly VisualElement anchor;
        readonly Func<MasterDataTypeDescriptor> typeProvider;
        readonly MasterRecordQueryCompletion completion = new MasterRecordQueryCompletion();

        public MasterSearchCompletionController(TextField searchField, Label hint, VisualElement anchor, Func<MasterDataTypeDescriptor> typeProvider)
        {
            this.searchField = searchField;
            this.hint = hint;
            this.anchor = anchor;
            this.typeProvider = typeProvider;

            hint.pickingMode = PickingMode.Ignore;
            hint.enableRichText = true;
            hint.style.display = DisplayStyle.None;

            searchField.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            searchField.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            searchField.RegisterCallback<KeyUpEvent>(OnKeyUp);
            searchField.RegisterCallback<PointerUpEvent>(OnPointerUp);
            searchField.RegisterCallback<FocusInEvent>(OnFocusIn);
            searchField.RegisterCallback<FocusOutEvent>(OnFocusOut);
            anchor.RegisterCallback<GeometryChangedEvent>(OnAnchorGeometryChanged);
        }

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
            completion.Reset();
            if (IsFocused()) UpdateHint();
            else Hide();
        }

        // ------------------------------------------------------------------ events

        void OnKeyDown(KeyDownEvent evt)
        {
            var isTab = evt.keyCode == KeyCode.Tab || (evt.keyCode == KeyCode.None && evt.character == '\t');
            if (!isTab || evt.ctrlKey || evt.altKey || evt.commandKey) return;

            if (evt.keyCode == KeyCode.Tab)
            {
                var text = searchField.value ?? string.Empty;
                if (completion.TryComplete(text, searchField.cursorIndex, typeProvider(), evt.shiftKey, out var newText, out var caret))
                {
                    searchField.value = newText;
                    SetCaret(caret);
                    // the text field may move the caret while it applies the new value
                    searchField.schedule.Execute(() => SetCaret(caret));
                }
                UpdateHint(newText, caret);
            }

            evt.StopPropagation();
            searchField.focusController?.IgnoreEvent(evt);
        }

        void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (evt.direction != NavigationMoveEvent.Direction.Next && evt.direction != NavigationMoveEvent.Direction.Previous) return;
            evt.StopPropagation();
            searchField.focusController?.IgnoreEvent(evt);
        }

        void OnKeyUp(KeyUpEvent evt)
        {
            if (evt.keyCode == KeyCode.Tab) return;
            UpdateHint();
        }

        void OnPointerUp(PointerUpEvent evt) => UpdateHint();

        void OnFocusIn(FocusInEvent evt) => searchField.schedule.Execute(() => UpdateHint());

        void OnFocusOut(FocusOutEvent evt)
        {
            completion.Reset();
            Hide();
        }

        void OnAnchorGeometryChanged(GeometryChangedEvent evt)
        {
            var bounds = anchor.layout;
            hint.style.top = bounds.yMax;
            hint.style.left = bounds.xMin;
        }

        // ------------------------------------------------------------------ hint

        void UpdateHint() => UpdateHint(searchField.value, searchField.cursorIndex);

        void UpdateHint(string text, int caret)
        {
            var context = MasterRecordQueryCompletion.GetContext(text, caret, typeProvider());
            if (context == null)
            {
                Hide();
                return;
            }

            hint.text = Format(context, completion.IsCycling(text, caret) ? completion.CurrentCandidate : null);
            hint.style.display = DisplayStyle.Flex;
        }

        static string Format(MasterRecordQueryCompletion.Context context, string current)
        {
            var sb = new StringBuilder();
            if (context.IsValue) sb.Append(context.ValueField.Name).Append(": ");
            var candidates = context.Candidates;

            if (!context.IsValue && candidates.Count == 1 && string.Equals(candidates[0], context.Word, StringComparison.Ordinal))
            {
                // the field name is complete: show what can follow it
                return sb.Append("<b>").Append(candidates[0]).Append("</b>  ").Append(Operators).ToString();
            }

            var shown = Math.Min(candidates.Count, MaxHintItems);
            for (var i = 0; i < shown; i++)
            {
                if (i > 0) sb.Append("   ");
                var bold = candidates[i] == current;
                if (bold) sb.Append("<b><u>");
                sb.Append(candidates[i]);
                if (bold) sb.Append("</u></b>");
            }
            if (candidates.Count > shown) sb.Append("   … +").Append(candidates.Count - shown);
            sb.Append("      <i>Tab</i>");
            return sb.ToString();
        }

        void Hide() => hint.style.display = DisplayStyle.None;

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
