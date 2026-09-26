using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Named patches stored as <c>Application.persistentDataPath/MasterMemoryDebugger/&lt;name&gt;.patch.json</c>.
    /// On WebGL the directory lives in IndexedDB and is flushed after every write.
    /// Methods taking a name use <see cref="MasterMemoryDebuggerSettings.DefaultPatchName"/> when it is null or empty.
    /// </summary>
    public static class MasterDataPatchStorage
    {
        public const string PatchExtension = ".patch.json";
        const string DirectoryName = "MasterMemoryDebugger";
        const string ExportDirectoryName = "exports";
        static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false);
        static readonly char[] s_invalidNameChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

        public static string DirectoryPath => Path.Combine(Application.persistentDataPath, DirectoryName);

        public static string ExportDirectoryPath => Path.Combine(DirectoryPath, ExportDirectoryName);

        public static string DefaultPatchName => NormalizeName(MasterMemoryDebuggerSettings.Current.DefaultPatchName) ?? "debug";

        /// <summary>Path of the default patch.</summary>
        public static string PatchPath => GetPatchPath(null);

        /// <summary>
        /// Turns user input into a file-name-safe patch name: trims, removes a ".patch.json" / ".json" suffix and
        /// replaces characters that are invalid in file names. Returns null for an empty name.
        /// </summary>
        public static string NormalizeName(string name)
        {
            if (name == null) return null;
            name = name.Trim();
            if (name.EndsWith(PatchExtension, StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - PatchExtension.Length);
            else if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - ".json".Length);

            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                sb.Append(c < 0x20 || Array.IndexOf(s_invalidNameChars, c) >= 0 ? '_' : c);
            }
            name = sb.ToString().Trim().Trim('.');
            return name.Length == 0 ? null : name;
        }

        public static string GetPatchPath(string name)
        {
            return Path.Combine(DirectoryPath, (NormalizeName(name) ?? DefaultPatchName) + PatchExtension);
        }

        /// <summary>Names of the saved patches, sorted.</summary>
        public static List<string> ListPatchNames()
        {
            var names = new List<string>();
            if (!MasterMemoryDebugBuild.IsEnabled || !Directory.Exists(DirectoryPath)) return names;
            foreach (var path in Directory.GetFiles(DirectoryPath, "*" + PatchExtension))
            {
                var fileName = Path.GetFileName(path);
                names.Add(fileName.Substring(0, fileName.Length - PatchExtension.Length));
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static bool Exists(string name = null) => MasterMemoryDebugBuild.IsEnabled && File.Exists(GetPatchPath(name));

        /// <summary>Saves (or overwrites) a named patch and returns its path.</summary>
        public static string Save(MasterDataPatch patch, string name = null)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return null;
            var path = GetPatchPath(name);
            WriteText(path, MasterDataPatchSerializer.ToJson(patch));
            return path;
        }

        /// <summary>Returns null when the patch does not exist.</summary>
        public static MasterDataPatch Load(string name = null)
        {
            if (!Exists(name)) return null;
            return MasterDataPatchSerializer.FromJson(File.ReadAllText(GetPatchPath(name), s_utf8));
        }

        public static bool Delete(string name = null)
        {
            if (!Exists(name)) return false;
            File.Delete(GetPatchPath(name));
            MasterDataWebGLBridge.SyncFileSystem();
            return true;
        }

        /// <summary>Writes a copy under the exports directory and returns its path.</summary>
        public static string WriteExport(string json, string fileName = null)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return null;
            var path = Path.Combine(ExportDirectoryPath, fileName ?? CreateExportFileName());
            WriteText(path, json);
            return path;
        }

        public static string CreateExportFileName(string patchName = null)
        {
            var prefix = NormalizeName(patchName) ?? "mastermemory-patch";
            return prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json";
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
