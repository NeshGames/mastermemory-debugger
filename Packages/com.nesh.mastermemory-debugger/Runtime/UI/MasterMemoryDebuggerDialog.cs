using System;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Modal dialog drawn on top of the debugger window.
    /// The first button is the cancel action (Esc); Enter runs the primary button only, so destructive dialogs
    /// (without a primary button) can not be confirmed by accident.
    /// </summary>
    internal sealed class MasterMemoryDebuggerDialog
    {
        public readonly struct DialogButton
        {
            public DialogButton(string text, Action action, bool isPrimary = false, bool isDanger = false)
            {
                Text = text;
                Action = action;
                IsPrimary = isPrimary;
                IsDanger = isDanger;
            }

            public string Text { get; }
            public Action Action { get; }
            public bool IsPrimary { get; }
            public bool IsDanger { get; }
        }

        readonly VisualElement layer;
        DialogButton[] currentButtons = new DialogButton[0];

        public MasterMemoryDebuggerDialog(VisualElement layer)
        {
            this.layer = layer;
            Hide();
        }

        public bool IsVisible => layer.style.display.value == DisplayStyle.Flex;

        public void Show(string title, string message, params DialogButton[] buttons)
        {
            Show(title, message, null, buttons);
        }

        /// <summary>Shows a dialog with additional content (for example input fields) below the message.</summary>
        public void Show(string title, string message, VisualElement content, params DialogButton[] buttons)
        {
            layer.Clear();
            currentButtons = buttons;

            var box = new VisualElement();
            box.AddToClassList("mm-debugger__dialog");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("mm-debugger__dialog-title");
            box.Add(titleLabel);

            if (!string.IsNullOrEmpty(message))
            {
                var messageLabel = new Label(message);
                messageLabel.AddToClassList("mm-debugger__dialog-message");
                box.Add(messageLabel);
            }
            if (content != null)
            {
                content.AddToClassList("mm-debugger__dialog-content");
                box.Add(content);
            }

            var buttonRow = new VisualElement();
            buttonRow.AddToClassList("mm-debugger__dialog-buttons");
            foreach (var definition in buttons)
            {
                var action = definition.Action;
                var button = new Button(() => Invoke(action))
                {
                    text = definition.Text,
                };
                button.AddToClassList("mm-debugger__button");
                if (definition.IsPrimary) button.AddToClassList("mm-debugger__button--primary");
                if (definition.IsDanger) button.AddToClassList("mm-debugger__button--danger");
                buttonRow.Add(button);
            }
            box.Add(buttonRow);

            layer.Add(box);
            layer.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            layer.style.display = DisplayStyle.None;
            layer.Clear();
            currentButtons = new DialogButton[0];
        }

        /// <summary>Runs the cancel (first) button.</summary>
        public void Cancel()
        {
            if (!IsVisible) return;
            if (currentButtons.Length == 0) Hide();
            else Invoke(currentButtons[0].Action);
        }

        /// <summary>Runs the primary button. Does nothing when the dialog has no primary button.</summary>
        public void Confirm()
        {
            if (!IsVisible) return;
            foreach (var button in currentButtons)
            {
                if (button.IsPrimary)
                {
                    Invoke(button.Action);
                    return;
                }
            }
        }

        void Invoke(Action action)
        {
            Hide();
            action?.Invoke();
        }
    }
}
