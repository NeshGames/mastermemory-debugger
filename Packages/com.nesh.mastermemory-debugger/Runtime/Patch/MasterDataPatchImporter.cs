using System;
using System.IO;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Brings a patch file (for example one exported on another device) into the saved patch list.
    /// Editor: file dialog. WebGL: browser file picker. Other platforms: paste the JSON into the debugger.
    /// </summary>
    public static class MasterDataPatchImporter
    {
        /// <summary>
        /// Opens a file picker and calls <paramref name="onOpened"/> with (file name, text).
        /// Returns false when the platform has no file picker (paste the JSON instead).
        /// </summary>
        public static bool TryOpenFile(Action<string, string> onOpened)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return false;
#if UNITY_EDITOR
            var path = UnityEditor.EditorUtility.OpenFilePanel("Import MasterMemory Patch", MasterDataPatchStorage.DirectoryPath, "json");
            if (!string.IsNullOrEmpty(path)) onOpened(Path.GetFileName(path), File.ReadAllText(path));
            return true;
#else
            if (!MasterDataWebGLBridge.IsAvailable) return false;
            MasterDataWebGLBridge.OpenFile(".json,application/json", onOpened);
            return true;
#endif
        }

        /// <summary>Parses and validates a patch. Throws <see cref="FormatException"/> for invalid content.</summary>
        public static MasterDataPatch Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("The patch is empty.");
            var patch = MasterDataPatchSerializer.FromJson(json);
            if (patch.FormatVersion <= 0 || patch.FormatVersion > MasterDataPatch.CurrentFormatVersion)
            {
                throw new FormatException($"Unsupported patch format version {patch.FormatVersion}.");
            }
            return patch;
        }

        /// <summary>Patch name derived from a file name ("balance-A.patch.json" → "balance-A").</summary>
        public static string SuggestName(string fileName)
        {
            return MasterDataPatchStorage.NormalizeName(Path.GetFileName(fileName ?? string.Empty)) ?? "imported";
        }
    }
}
