using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Reads and writes patches under <c>Application.persistentDataPath/MasterMemoryDebugger/</c>.
    /// On WebGL the directory lives in IndexedDB and is flushed after every write.
    /// </summary>
    public static class MasterDataPatchStorage
    {
        const string DirectoryName = "MasterMemoryDebugger";
        const string ExportDirectoryName = "exports";
        static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false);

        public static string DirectoryPath => Path.Combine(Application.persistentDataPath, DirectoryName);

        public static string PatchPath => Path.Combine(DirectoryPath, MasterMemoryDebuggerSettings.Current.PatchFileName);

        public static string ExportDirectoryPath => Path.Combine(DirectoryPath, ExportDirectoryName);

        public static bool Exists() => MasterMemoryDebugBuild.IsEnabled && File.Exists(PatchPath);

        public static void Save(MasterDataPatch patch)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return;
            WriteText(PatchPath, MasterDataPatchSerializer.ToJson(patch));
        }

        /// <summary>Returns null when no patch was saved.</summary>
        public static MasterDataPatch Load()
        {
            if (!Exists()) return null;
            return MasterDataPatchSerializer.FromJson(File.ReadAllText(PatchPath, s_utf8));
        }

        public static bool Delete()
        {
            if (!Exists()) return false;
            File.Delete(PatchPath);
            MasterDataWebGLBridge.SyncFileSystem();
            return true;
        }

        /// <summary>Writes a timestamped copy under the exports directory and returns its path.</summary>
        public static string WriteExport(string json, string fileName = null)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return null;
            var path = Path.Combine(ExportDirectoryPath, fileName ?? CreateExportFileName());
            WriteText(path, json);
            return path;
        }

        public static string CreateExportFileName()
        {
            return "mastermemory-patch-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json";
        }

        static void WriteText(string path, string text)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // write to a temp file first so that a crash never leaves a truncated patch
            var temp = path + ".tmp";
            File.WriteAllText(temp, text, s_utf8);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
            MasterDataWebGLBridge.SyncFileSystem();
        }
    }
}
