using UnityEditor;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger.Editor
{
    static class MasterMemoryDebuggerMenu
    {
        const string Root = "Tools/MasterMemory Debugger/";

        [MenuItem(Root + "Toggle Runtime Debugger", false, 0)]
        static void ToggleDebugger() => RuntimeMasterMemoryDebugger.Toggle();

        [MenuItem(Root + "Toggle Runtime Debugger", true)]
        static bool ToggleDebuggerValidate() => EditorApplication.isPlaying;

        [MenuItem(Root + "Open Patch Folder", false, 20)]
        static void OpenPatchFolder() => MasterDataPatchExporter.RevealDataDirectory();

        [MenuItem(Root + "Delete Default Patch", false, 21)]
        static void DeleteSavedPatch()
        {
            if (!MasterDataPatchStorage.Exists())
            {
                EditorUtility.DisplayDialog("MasterMemory Debugger", "No saved patch.\n" + MasterDataPatchStorage.PatchPath, "OK");
                return;
            }
            if (EditorUtility.DisplayDialog("MasterMemory Debugger", "Delete the default patch?\n" + MasterDataPatchStorage.PatchPath, "Delete", "Cancel"))
            {
                MasterDataPatchStorage.Delete();
                Debug.Log("[MasterMemoryDebugger] Saved patch deleted.");
            }
        }

        [MenuItem(Root + "Settings", false, 40)]
        static void OpenSettings() => SettingsService.OpenProjectSettings(MasterMemoryDebuggerSettingsProvider.SettingsPath);
    }
}
