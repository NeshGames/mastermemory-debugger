using System;
using System.Diagnostics;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// "Find" view: a value in every table (<see cref="MasterMemoryGlobalSearch"/>), grouped by table. Open jumps to the record.
    /// Searches on Enter / Find, and again when the overrides change while visible.
    /// </summary>
    internal sealed class MasterFindController
    {
        readonly VisualElement panel;
        readonly TextField queryField;
        readonly Toggle wholeValueToggle;
        readonly Label summaryLabel;
        readonly ScrollView list;
        readonly Action<MasterMemoryTableDescriptor, object> open;
        bool isStale = true;

        public MasterFindController(VisualElement panel, Action<MasterMemoryTableDescriptor, object> open, string query, bool wholeValue)
        {
            this.panel = panel;
            this.open = open;

            var header = new VisualElement();
            header.AddToClassList("mm-debugger__changes-header");
            header.AddToClassList("mm-debugger__find-header");
            var title = new Label("Find");
            title.AddToClassList("mm-debugger__section-title");
            header.Add(title);
            queryField = new TextField { name = "mm-find-query", value = query ?? string.Empty, tooltip = "A value to find in every table: a key, a name, a number… (Enter)" };
            queryField.AddToClassList("mm-debugger__search");
            queryField.AddToClassList("mm-debugger__find-query");
            queryField.RegisterCallback<KeyDownEvent>(OnQueryKeyDown, TrickleDown.TrickleDown);
            header.Add(queryField);
            wholeValueToggle = new Toggle("Whole value") { value = wholeValue, tooltip = "Only values that equal the text (1001 does not find 11001)" };
            wholeValueToggle.AddToClassList("mm-debugger__modified-only");
            wholeValueToggle.RegisterValueChangedCallback(_ => Search());
            header.Add(wholeValueToggle);
            var findButton = new Button(Search) { text = "Find", tooltip = "Find the text in every member of every table (overrides included)" };
            findButton.AddToClassList("mm-debugger__button");
            findButton.AddToClassList("mm-debugger__button--primary");
            header.Add(findButton);
            summaryLabel = new Label();
            summaryLabel.AddToClassList("mm-debugger__header-info");
            header.Add(summaryLabel);
            panel.Add(header);

            list = new ScrollView(ScrollViewMode.Vertical);
            list.AddToClassList("mm-debugger__changes-list");
            panel.Add(list);
            Hide();
        }

        public string Query => queryField.value;

        public bool WholeValue => wholeValueToggle.value;

        public bool IsVisible => panel.style.display.value == DisplayStyle.Flex;

        /// <summary>The query box (keys typed there are not debugger shortcuts).</summary>
        public VisualElement QueryField => queryField;

        public void Show()
        {
            panel.style.display = DisplayStyle.Flex;
            if (isStale) Search();
            queryField.schedule.Execute(() => queryField.Focus());
        }

        public void Hide()
        {
            panel.style.display = DisplayStyle.None;
        }

        /// <summary>Overrides or tables changed: search again now when visible, otherwise when shown.</summary>
        public void MarkStale()
        {
            isStale = true;
            if (IsVisible) Search();
        }

        void OnQueryKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != UnityEngine.KeyCode.Return && evt.keyCode != UnityEngine.KeyCode.KeypadEnter) return;
            Search();
            evt.StopPropagation();
        }

        void Search()
        {
            isStale = false;
            list.Clear();
            var query = queryField.value?.Trim();
            if (string.IsNullOrEmpty(query))
            {
                summaryLabel.text = string.Empty;
                AddHint("Type a value and press Enter: every member of every table is searched (current values, list elements, " +
                        "members of nested objects). Whole value finds exact values only, for example an ID.");
                return;
            }

            var watch = Stopwatch.StartNew();
            var result = MasterMemoryGlobalSearch.Find(query, wholeValueToggle.value);
            watch.Stop();

            summaryLabel.text = result.TotalHits == 0
                ? $"Not found  ({watch.ElapsedMilliseconds} ms)"
                : $"{result.TotalHits} values in {result.RecordCount} records of {result.TableCount} tables  ({watch.ElapsedMilliseconds} ms)";
            if (result.TotalHits == 0)
            {
                AddHint(wholeValueToggle.value ? $"No value equals \"{query}\"." : $"No value contains \"{query}\".");
                return;
            }

            MasterMemoryTableDescriptor table = null;
            VisualElement group = null;
            MasterMemoryRecordDescriptor record = null;
            VisualElement entry = null;
            foreach (var hit in result.Hits)
            {
                if (hit.Table != table)
                {
                    table = hit.Table;
                    group = new VisualElement();
                    group.AddToClassList("mm-debugger__find-table");
                    var tableLabel = new Label(MasterMemoryDebugLocalization.GetTableLabel(table)) { tooltip = table.TableName };
                    tableLabel.AddToClassList("mm-debugger__section-title");
                    group.Add(tableLabel);
                    list.Add(group);
                    record = null;
                }
                if (hit.Record != record)
                {
                    record = hit.Record;
                    entry = CreateRecordEntry(record);
                    group.Add(entry);
                }
                entry.Add(CreateHitRow(hit));
            }
            if (result.IsTruncated) AddHint($"Only the first {result.Hits.Count} of {result.TotalHits} values are shown. Use Whole value or a longer text.");
        }

        VisualElement CreateRecordEntry(MasterMemoryRecordDescriptor record)
        {
            var root = new VisualElement();
            root.AddToClassList("mm-debugger__change");
            var header = new VisualElement();
            header.AddToClassList("mm-debugger__change-header");
            var key = new Label(record.KeyText);
            key.AddToClassList("mm-debugger__change-key");
            header.Add(key);
            var name = new Label(record.Table.HasCustomDisplayName ? record.GetDisplayName() ?? string.Empty : string.Empty);
            name.AddToClassList("mm-debugger__change-name");
            header.Add(name);
            if (record.IsModified)
            {
                var badge = new Label("Overridden") { tooltip = "The values shown include the override" };
                badge.AddToClassList("mm-debugger__badge");
                badge.AddToClassList("mm-debugger__badge--modified");
                header.Add(badge);
            }
            var openButton = new Button(() => open(record.Table, record.PrimaryKey)) { text = MasterMemoryDebugUiLocalization.Text("Open") };
            openButton.AddToClassList("mm-debugger__button");
            header.Add(openButton);
            root.Add(header);
            return root;
        }

        static VisualElement CreateHitRow(MasterMemorySearchHit hit)
        {
            var row = new VisualElement();
            row.AddToClassList("mm-debugger__change-field");
            var name = new Label(MasterMemoryDebugLocalization.GetFieldLabel(hit.Table, hit.Field) + hit.Path) { tooltip = hit.Field.Name + hit.Path };
            name.AddToClassList("mm-debugger__change-field-name");
            row.Add(name);
            var value = new Label(MasterDataValueUtility.Format(hit.Value));
            value.AddToClassList("mm-debugger__change-new");
            value.selection.isSelectable = true;
            row.Add(value);
            return row;
        }

        void AddHint(string text)
        {
            var hint = new Label(text);
            hint.AddToClassList("mm-debugger__hint");
            list.Add(hint);
        }
    }
}
