using System;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// "Validation" view: every failure of MasterMemory's <c>Validate()</c> on the rebuilt databases
    /// (<see cref="MasterMemoryDebugValidation"/>), failures caused by the overrides first. Open jumps to the failing record.
    /// Validated only while visible.
    /// </summary>
    internal sealed class MasterValidationController
    {
        const int MaxShown = 500;

        readonly VisualElement panel;
        readonly Label summaryLabel;
        readonly Toggle newOnlyToggle;
        readonly ScrollView list;
        readonly Action<MasterMemoryTableDescriptor, object> open;

        public MasterValidationController(VisualElement panel, Action<MasterMemoryTableDescriptor, object> open)
        {
            this.panel = panel;
            this.open = open;

            var header = new VisualElement();
            header.AddToClassList("mm-debugger__changes-header");
            var title = new Label("Validation");
            title.AddToClassList("mm-debugger__section-title");
            header.Add(title);
            summaryLabel = new Label();
            summaryLabel.AddToClassList("mm-debugger__header-info");
            header.Add(summaryLabel);
            var spacer = new VisualElement();
            spacer.AddToClassList("mm-debugger__spacer");
            header.Add(spacer);
            newOnlyToggle = new Toggle("New only") { tooltip = "Only the failures that the original data does not have (caused by the overrides)" };
            newOnlyToggle.AddToClassList("mm-debugger__modified-only");
            newOnlyToggle.RegisterValueChangedCallback(_ => Refresh());
            header.Add(newOnlyToggle);
            var runButton = new Button(Refresh) { text = "Validate", tooltip = "Run MasterMemory Validate() again" };
            runButton.AddToClassList("mm-debugger__button");
            header.Add(runButton);
            panel.Add(header);

            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("mm-debugger__changes-list");
            panel.Add(list);
            Hide();
        }

        public bool IsVisible => panel.style.display.value == DisplayStyle.Flex;

        public void Show()
        {
            panel.style.display = DisplayStyle.Flex;
            Refresh();
        }

        public void Hide()
        {
            panel.style.display = DisplayStyle.None;
            list.Clear();
        }

        public void Refresh()
        {
            if (!IsVisible) return;
            list.Clear();

            if (!MasterMemoryDebugValidation.IsAvailable)
            {
                summaryLabel.text = string.Empty;
                AddHint("No database is validated. Rebuild the gameplay database with\n" +
                        "MasterMemoryDebugRebuild.AutoRebuild(database, db => ...)  (validate: true)\n" +
                        "to list the failures of MasterMemory Validate() (IValidatable<T>) here.");
                return;
            }

            var failures = MasterMemoryDebugValidation.Run();
            var newCount = 0;
            foreach (var failure in failures) if (failure.IsNew) newCount++;
            summaryLabel.text = failures.Count == 0 ? "No failures" : $"{failures.Count} failures, {newCount} caused by overrides";

            var shown = 0;
            foreach (var failure in failures)
            {
                if (newOnlyToggle.value && !failure.IsNew) continue;
                if (shown == MaxShown)
                {
                    AddHint($"Only the first {MaxShown} failures are shown.");
                    break;
                }
                list.Add(CreateEntry(failure));
                shown++;
            }
            if (shown == 0) AddHint(failures.Count == 0 ? "Validate() reports no failure." : "No failure is caused by the overrides.");
        }

        VisualElement CreateEntry(MasterMemoryValidationFailure failure)
        {
            var root = new VisualElement();
            root.AddToClassList("mm-debugger__change");
            root.AddToClassList("mm-debugger__validation");
            root.EnableInClassList("mm-debugger__validation--new", failure.IsNew);

            var header = new VisualElement();
            header.AddToClassList("mm-debugger__change-header");
            if (failure.IsNew)
            {
                var badge = new Label("NEW") { tooltip = "Caused by the overrides: the original data does not have this failure" };
                badge.AddToClassList("mm-debugger__badge");
                badge.AddToClassList("mm-debugger__badge--new");
                header.Add(badge);
            }

            MasterMemoryTableDescriptor table = null;
            var hasTable = failure.RecordType != null && MasterMemoryDebugRegistry.TryGetTable(failure.RecordType, out table);
            var title = new Label(hasTable ? MasterMemoryDebugLocalization.GetTableLabel(table) : failure.RecordType?.Name ?? "?");
            title.AddToClassList("mm-debugger__change-table");
            header.Add(title);

            var key = hasTable ? TryGetKey(table, failure.Record) : null;
            if (key != null)
            {
                var keyLabel = new Label(MasterDataValueUtility.FormatKey(key));
                keyLabel.AddToClassList("mm-debugger__change-key");
                header.Add(keyLabel);
            }
            var spacer = new VisualElement();
            spacer.AddToClassList("mm-debugger__spacer");
            header.Add(spacer);
            if (key != null)
            {
                var openButton = new Button(() => open(table, key)) { text = "Open" };
                openButton.AddToClassList("mm-debugger__button");
                header.Add(openButton);
            }
            root.Add(header);

            var message = new Label(failure.Message);
            message.AddToClassList("mm-debugger__validation-message");
            message.selection.isSelectable = true;
            root.Add(message);
            return root;
        }

        static object TryGetKey(MasterMemoryTableDescriptor table, object record)
        {
            if (record == null || !table.RecordType.IsInstanceOfType(record)) return null;
            try
            {
                return table.GetPrimaryKey(record);
            }
            catch (Exception)
            {
                return null;
            }
        }

        void AddHint(string text)
        {
            var hint = new Label(text);
            hint.AddToClassList("mm-debugger__hint");
            list.Add(hint);
        }
    }
}
