using System;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Saved Data views: table, query/filter, sort and column layout.</summary>
    internal sealed class MasterSavedViewsController : IDisposable
    {
        readonly Button button;
        readonly MasterMemoryDebuggerDialog dialog;
        readonly Func<MasterMemoryTableDescriptor> getTable;
        readonly Func<MasterRecordViewState> capture;
        readonly Action<MasterSavedView> load;
        readonly Action<string, bool> setStatus;

        public MasterSavedViewsController(
            Button button,
            MasterMemoryDebuggerDialog dialog,
            Func<MasterMemoryTableDescriptor> getTable,
            Func<MasterRecordViewState> capture,
            Action<MasterSavedView> load,
            Action<string, bool> setStatus)
        {
            this.button = button;
            this.dialog = dialog;
            this.getTable = getTable;
            this.capture = capture;
            this.load = load;
            this.setStatus = setStatus;
            button.clicked += Show;
        }

        public void Dispose() => button.clicked -= Show;

        void Show()
        {
            var table = getTable();
            var content = new VisualElement();
            var name = new TextField("View name")
            {
                value = table?.TableName ?? string.Empty,
                tooltip = "A saved view stores table, query, Modified Only, sort and column layout.",
            };
            content.Add(name);

            var views = MasterSavedViews.Snapshot();
            if (views.Count == 0)
            {
                var empty = new Label("No saved views yet.");
                empty.AddToClassList("mm-debugger__hint");
                content.Add(empty);
            }
            else
            {
                var list = new ScrollView(ScrollViewMode.Vertical);
                list.AddToClassList("mm-debugger__changes-list");
                foreach (var view in views)
                {
                    var saved = view;
                    var row = new VisualElement();
                    row.AddToClassList("mm-debugger__change-field");

                    var label = new Label(Describe(saved));
                    label.AddToClassList("mm-debugger__change-field-name");
                    row.Add(label);

                    var loadButton = new Button(() =>
                    {
                        dialog.Hide();
                        load(saved);
                    }) { text = "Load", tooltip = "Restore this saved Data view" };
                    loadButton.AddToClassList("mm-debugger__button");
                    row.Add(loadButton);

                    var delete = new Button(() =>
                    {
                        MasterSavedViews.Delete(saved.Name);
                        setStatus($"Saved view \"{saved.Name}\" deleted.", false);
                        dialog.Hide();
                        Show();
                    }) { text = "Delete", tooltip = "Delete this saved view" };
                    delete.AddToClassList("mm-debugger__button");
                    delete.AddToClassList("mm-debugger__button--danger");
                    row.Add(delete);
                    list.Add(row);
                }
                content.Add(list);
            }

            dialog.Show(
                "Saved views",
                "Save or restore a Data view. Views are development-only and stored on this device.",
                content,
                new MasterMemoryDebuggerDialog.DialogButton("Close", null),
                new MasterMemoryDebuggerDialog.DialogButton(
                    "Save Current",
                    () => SaveCurrent(name.value),
                    isPrimary: true));
        }

        void SaveCurrent(string rawName)
        {
            var table = getTable();
            if (table == null)
            {
                setStatus("Select a table before saving a view.", true);
                return;
            }
            var name = MasterSavedViews.NormalizeName(rawName);
            if (name == null)
            {
                setStatus("Enter a saved view name.", true);
                return;
            }

            var state = capture();
            var view = new MasterSavedView
            {
                Name = name,
                TableName = table.TableName,
                Query = state.Query,
                ModifiedOnly = state.ModifiedOnly,
                SortKey = state.SortKey,
                SortDescending = state.SortDescending,
                ColumnLayout = state.ColumnLayout,
            };

            if (MasterSavedViews.Find(name) != null)
            {
                dialog.Show(
                    "Overwrite saved view",
                    $"A saved view named \"{name}\" already exists. Replace it with the current Data view?",
                    new MasterMemoryDebuggerDialog.DialogButton("Cancel", null),
                    new MasterMemoryDebuggerDialog.DialogButton(
                        "Overwrite",
                        () => Store(view),
                        isDanger: true));
                return;
            }
            Store(view);
        }

        void Store(MasterSavedView view)
        {
            if (!MasterSavedViews.Save(view))
            {
                setStatus("Saved view could not be stored.", true);
                return;
            }
            setStatus($"Saved view \"{view.Name}\" stored.", false);
        }

        static string Describe(MasterSavedView view)
        {
            var filter = string.IsNullOrWhiteSpace(view.Query) ? "all records" : view.Query;
            var sort = string.IsNullOrEmpty(view.SortKey)
                ? "unsorted"
                : view.SortKey + (view.SortDescending ? " ↓" : " ↑");
            return $"{view.Name}   ·   {view.TableName}   ·   {filter}   ·   {sort}";
        }
    }
}
