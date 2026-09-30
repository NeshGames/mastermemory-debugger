using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    internal enum MasterDebuggerTab
    {
        Data,
        Changes,
        Patches,
        Find,
        Validation,
        Diagnostics,
    }

    /// <summary>Owns tab visibility and tab-button presentation; feature controllers retain their own content behavior.</summary>
    internal sealed class MasterDebuggerNavigationController
    {
        readonly VisualElement mainPanel;
        readonly VisualElement patchesPanel;
        readonly Button dataTab;
        readonly Button changesTab;
        readonly Button patchesTab;
        readonly Button validationTab;
        readonly Button findTab;
        readonly Button diagnosticsTab;
        readonly MasterChangesController changes;
        readonly MasterPatchesController patches;
        readonly MasterValidationController validation;
        readonly MasterFindController find;
        readonly MasterDiagnosticsController diagnostics;
        readonly MasterRecordListController recordList;

        public MasterDebuggerNavigationController(
            VisualElement root,
            MasterRecordListController recordList,
            MasterChangesController changes,
            MasterPatchesController patches,
            MasterValidationController validation,
            MasterFindController find,
            MasterDiagnosticsController diagnostics)
        {
            this.recordList = recordList;
            this.changes = changes;
            this.patches = patches;
            this.validation = validation;
            this.find = find;
            this.diagnostics = diagnostics;
            mainPanel = Required<VisualElement>(root, "mm-main");
            patchesPanel = Required<VisualElement>(root, "mm-patches-panel");
            dataTab = Required<Button>(root, "mm-tab-data");
            changesTab = Required<Button>(root, "mm-tab-changes");
            patchesTab = Required<Button>(root, "mm-tab-patches");
            validationTab = Required<Button>(root, "mm-tab-validation");
            findTab = Required<Button>(root, "mm-tab-find");
            diagnosticsTab = Required<Button>(root, "mm-tab-diagnostics");
        }

        public MasterDebuggerTab CurrentTab { get; private set; } = MasterDebuggerTab.Data;

        public void Select(MasterDebuggerTab tab)
        {
            CurrentTab = tab;
            mainPanel.style.display = tab == MasterDebuggerTab.Data ? DisplayStyle.Flex : DisplayStyle.None;
            if (tab == MasterDebuggerTab.Changes) changes.Show();
            else changes.Hide();

            patchesPanel.style.display = tab == MasterDebuggerTab.Patches ? DisplayStyle.Flex : DisplayStyle.None;
            if (tab == MasterDebuggerTab.Patches) patches.Refresh();

            if (tab == MasterDebuggerTab.Validation) validation.Show();
            else validation.Hide();

            if (tab == MasterDebuggerTab.Find) find.Show();
            else find.Hide();

            if (tab == MasterDebuggerTab.Diagnostics) diagnostics.Show();
            else diagnostics.Hide();

            recordList.CloseColumnsPopup();
        }

        public void Refresh(int overrideCount, int patchCount, int newFailureCount)
        {
            changesTab.text = $"Changes ({overrideCount})";
            patchesTab.text = $"Patches ({patchCount})";
            dataTab.EnableInClassList("mm-debugger__tab--selected", CurrentTab == MasterDebuggerTab.Data);
            changesTab.EnableInClassList("mm-debugger__tab--selected", CurrentTab == MasterDebuggerTab.Changes);
            patchesTab.EnableInClassList("mm-debugger__tab--selected", CurrentTab == MasterDebuggerTab.Patches);
            validationTab.EnableInClassList("mm-debugger__tab--selected", CurrentTab == MasterDebuggerTab.Validation);
            findTab.EnableInClassList("mm-debugger__tab--selected", CurrentTab == MasterDebuggerTab.Find);
            diagnosticsTab.EnableInClassList("mm-debugger__tab--selected", CurrentTab == MasterDebuggerTab.Diagnostics);
            validationTab.text = newFailureCount > 0 ? $"Validation ({newFailureCount} new)" : "Validation";
            validationTab.EnableInClassList("mm-debugger__tab--alert", newFailureCount > 0);
        }

        static T Required<T>(VisualElement root, string name) where T : VisualElement =>
            root.Q<T>(name) ?? throw new System.InvalidOperationException(
                $"MasterMemoryDebugger.uxml is missing <{typeof(T).Name} name=\"{name}\">.");
    }
}
