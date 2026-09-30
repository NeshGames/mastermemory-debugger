using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Owns root keyboard shortcuts so the composition controller does not also implement input routing.</summary>
    internal sealed class MasterDebuggerShortcutController : IDisposable
    {
        readonly VisualElement root;
        readonly MasterMemoryDebuggerDialog dialog;
        readonly MasterTableListController tableList;
        readonly MasterSearchCompletionController searchCompletion;
        readonly MasterRecordListController recordList;
        readonly MasterRecordEditorController editor;
        readonly MasterFindController find;
        readonly Action undo;
        readonly Action redo;

        public MasterDebuggerShortcutController(
            VisualElement root,
            MasterMemoryDebuggerDialog dialog,
            MasterTableListController tableList,
            MasterSearchCompletionController searchCompletion,
            MasterRecordListController recordList,
            MasterRecordEditorController editor,
            MasterFindController find,
            Action undo,
            Action redo)
        {
            this.root = root;
            this.dialog = dialog;
            this.tableList = tableList;
            this.searchCompletion = searchCompletion;
            this.recordList = recordList;
            this.editor = editor;
            this.find = find;
            this.undo = undo;
            this.redo = redo;
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        public void Dispose()
        {
            root.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Z:
                case KeyCode.Y:
                    if (!evt.actionKey || dialog.IsVisible || IsInSearch(evt.target as VisualElement)) break;
                    if (evt.keyCode == KeyCode.Y || evt.shiftKey) redo();
                    else undo();
                    evt.StopPropagation();
                    break;

                case KeyCode.Escape:
                    if (!dialog.IsVisible && tableList.IsSearchTarget(evt.target as VisualElement)
                        && !string.IsNullOrEmpty(root.Q<TextField>("mm-table-search").value))
                    {
                        tableList.ClearSearch();
                        evt.StopPropagation();
                        break;
                    }
                    if (dialog.IsVisible) dialog.Cancel();
                    else if (searchCompletion.IsOpen) searchCompletion.Close();
                    else if (recordList.IsColumnsPopupOpen) recordList.CloseColumnsPopup();
                    else if (!MasterMemoryDebugRemote.IsToolMode) RuntimeMasterMemoryDebugger.Close();
                    evt.StopPropagation();
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    var target = evt.target as VisualElement;
                    if (dialog.IsVisible)
                    {
                        if (target?.GetFirstAncestorOfType<TextField>()?.multiline == true
                            || (target as TextField)?.multiline == true)
                            break;
                        dialog.Confirm();
                        evt.StopPropagation();
                    }
                    else if (target != null && editor.Container.Contains(target))
                    {
                        var textField = target as TextField ?? target.GetFirstAncestorOfType<TextField>();
                        var inTextEditor = textField != null
                            && textField.ClassListContains("mm-debugger__text-editor");
                        var wasDirty = editor.IsDirty;
                        if (wasDirty) editor.TryApply();
                        if (wasDirty || inTextEditor) evt.StopPropagation();
                    }
                    break;
            }
        }

        bool IsInSearch(VisualElement target)
        {
            if (target == null) return false;
            if (find.QueryField.Contains(target) || tableList.IsSearchTarget(target)) return true;
            var toolbar = root.Q("mm-search-toolbar");
            return toolbar != null && toolbar.Contains(target);
        }
    }
}
