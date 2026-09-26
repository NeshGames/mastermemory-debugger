using System;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Modal dialog drawn on top of the debugger window.</summary>
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

        public MasterMemoryDebuggerDialog(VisualElement layer)
        {
            this.layer = layer;
            Hide();
        }

        public bool IsVisible => layer.style.display.value == DisplayStyle.Flex;

        public void Show(string title, string message, params DialogButton[] buttons)
        {
            layer.Clear();

            var box = new VisualElement();
            box.AddToClassList("mm-debugger__dialog");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("mm-debugger__dialog-title");
            box.Add(titleLabel);

            var messageLabel = new Label(message);
            messageLabel.AddToClassList("mm-debugger__dialog-message");
            box.Add(messageLabel);

            var buttonRow = new VisualElement();
            buttonRow.AddToClassList("mm-debugger__dialog-buttons");
            foreach (var definition in buttons)
            {
                var action = definition.Action;
                var button = new Button(() =>
                {
                    Hide();
                    action?.Invoke();
                })
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
        }
    }
}
