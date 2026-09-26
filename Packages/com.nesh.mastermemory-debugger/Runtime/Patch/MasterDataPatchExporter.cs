using System;
using System.IO;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    public readonly struct MasterDataExportResult
    {
        public MasterDataExportResult(bool succeeded, string message, string path)
        {
            Succeeded = succeeded;
            Message = message;
            Path = path;
        }

        public bool Succeeded { get; }
        public string Message { get; }

        /// <summary>Written file path. Null for WebGL downloads and cancelled dialogs.</summary>
        public string Path { get; }
    }

    /// <summary>
    /// Hands a patch file to the user.
    /// Editor: save file dialog. WebGL: browser download. Other platforms: file under persistentDataPath/MasterMemoryDebugger/exports.
    /// </summary>
    public static class MasterDataPatchExporter
    {
        public static MasterDataExportResult Export(string json, string fileName = null)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return new MasterDataExportResult(false, "Debugger is disabled in this build.", null);
            fileName = string.IsNullOrEmpty(fileName) ? MasterDataPatchStorage.CreateExportFileName() : fileName;

            try
            {
#if UNITY_EDITOR
                var path = UnityEditor.EditorUtility.SaveFilePanel("Export MasterMemory Patch", "", fileName, "json");
                if (string.IsNullOrEmpty(path)) return new MasterDataExportResult(false, "Export cancelled.", null);
                File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
                return new MasterDataExportResult(true, "Exported: " + path, path);
#else
                if (MasterDataWebGLBridge.IsAvailable)
                {
                    MasterDataWebGLBridge.DownloadFile(fileName, json, "application/json");
                    return new MasterDataExportResult(true, "Downloaded: " + fileName, null);
                }
                var exported = MasterDataPatchStorage.WriteExport(json, fileName);
                return new MasterDataExportResult(true, "Exported: " + exported, exported);
#endif
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Error("Export failed: " + e);
                return new MasterDataExportResult(false, "Export failed: " + e.Message, null);
            }
        }

        /// <summary>True when the exported file location can be opened with the OS file browser.</summary>
        public static bool CanRevealExports
        {
            get
            {
#if UNITY_EDITOR
                return true;
#else
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer:
                    case RuntimePlatform.OSXPlayer:
                    case RuntimePlatform.LinuxPlayer:
                        return true;
                    default:
                        return false;
                }
#endif
            }
        }

        /// <summary>Opens the MasterMemoryDebugger data directory.</summary>
        public static void RevealDataDirectory()
        {
            var directory = MasterDataPatchStorage.DirectoryPath;
            Directory.CreateDirectory(directory);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(directory);
#else
            Application.OpenURL(new Uri(directory).AbsoluteUri);
#endif
        }
    }
}
