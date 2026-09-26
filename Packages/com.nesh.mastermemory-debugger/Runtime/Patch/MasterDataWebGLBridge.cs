using System;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Calls into <c>Plugins/WebGL/MasterMemoryDebugger.jslib</c>. No-op on other platforms.</summary>
    internal static class MasterDataWebGLBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void MMDebugger_DownloadFile(string fileName, string content, string mimeType);

        [DllImport("__Internal")]
        static extern void MMDebugger_SyncFileSystem();

        delegate void FileOpenedCallback(string payload);

        [DllImport("__Internal")]
        static extern void MMDebugger_OpenFile(string accept, FileOpenedCallback callback);

        static Action<string, string> s_onFileOpened;

        /// <summary>Opens the browser file picker; <paramref name="onOpened"/> receives (file name, text).</summary>
        public static void OpenFile(string accept, Action<string, string> onOpened)
        {
            s_onFileOpened = onOpened;
            MMDebugger_OpenFile(accept, OnFileOpened);
        }

        [MonoPInvokeCallback(typeof(FileOpenedCallback))]
        static void OnFileOpened(string payload)
        {
            var callback = s_onFileOpened;
            s_onFileOpened = null;
            if (callback == null || payload == null) return;
            var separator = payload.IndexOf('\n');
            callback(separator < 0 ? payload : payload.Substring(0, separator), separator < 0 ? string.Empty : payload.Substring(separator + 1));
        }

        public static bool IsAvailable => true;

        public static void DownloadFile(string fileName, string content, string mimeType)
        {
            MMDebugger_DownloadFile(fileName, content, mimeType);
        }

        /// <summary>Flushes Application.persistentDataPath to IndexedDB.</summary>
        public static void SyncFileSystem()
        {
            MMDebugger_SyncFileSystem();
        }
#else
        public static bool IsAvailable => false;

        public static void DownloadFile(string fileName, string content, string mimeType)
        {
        }

        public static void SyncFileSystem()
        {
        }

        public static void OpenFile(string accept, Action<string, string> onOpened)
        {
        }
#endif
    }
}
