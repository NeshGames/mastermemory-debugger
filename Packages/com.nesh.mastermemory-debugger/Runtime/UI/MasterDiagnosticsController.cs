using System;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Development diagnostics: identity, protocol state and the most recent timed operations.</summary>
    internal sealed class MasterDiagnosticsController : IDisposable
    {
        readonly VisualElement panel;
        readonly Label summary;
        readonly ScrollView list;
        readonly Button clear;

        public MasterDiagnosticsController(VisualElement panel)
        {
            this.panel = panel;
            panel.Clear();

            var header = new VisualElement();
            header.AddToClassList("mm-debugger__changes-header");
            var title = new Label("Diagnostics");
            title.AddToClassList("mm-debugger__section-title");
            header.Add(title);
            summary = new Label();
            summary.AddToClassList("mm-debugger__header-info");
            header.Add(summary);
            var spacer = new VisualElement();
            spacer.AddToClassList("mm-debugger__spacer");
            header.Add(spacer);
            clear = new Button(MasterMemoryDiagnostics.Clear) { text = "Clear" };
            clear.AddToClassList("mm-debugger__button");
            header.Add(clear);
            panel.Add(header);

            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("mm-debugger__changes-list");
            panel.Add(list);
            panel.style.display = DisplayStyle.None;
            MasterMemoryDiagnostics.Changed += Refresh;
        }

        public void Show()
        {
            panel.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Hide() => panel.style.display = DisplayStyle.None;

        public void Dispose()
        {
            MasterMemoryDiagnostics.Changed -= Refresh;
        }

        void Refresh()
        {
            summary.text =
                $"Schema {Short(MasterMemoryDebugRegistry.GetSchemaHash())}   Protocol v{MasterMemoryRemoteProtocol.Version}   " +
                $"Overrides {MasterMemoryDebugRuntime.OverrideCount}   Remote {MasterMemoryDebugRemote.State}";
            list.Clear();

            var entries = MasterMemoryDiagnostics.Snapshot();
            if (entries.Count == 0)
            {
                var empty = new Label("No timed operations recorded yet.");
                empty.AddToClassList("mm-debugger__hint");
                list.Add(empty);
                return;
            }

            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                var label = new Label(
                    $"{entry.TimeUtc:HH:mm:ss.fff}  {entry.Category}/{entry.Operation}  {entry.Milliseconds:0.0} ms" +
                    (string.IsNullOrEmpty(entry.Detail) ? string.Empty : "  " + entry.Detail));
                label.AddToClassList("mm-debugger__log-entry");
                label.selection.isSelectable = true;
                list.Add(label);
            }
        }

        static string Short(string value) => string.IsNullOrEmpty(value) || value.Length <= 12 ? value : value.Substring(0, 12);
    }
}
