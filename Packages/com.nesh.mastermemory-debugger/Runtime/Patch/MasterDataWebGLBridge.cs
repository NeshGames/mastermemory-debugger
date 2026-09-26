#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
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
#endif
    }
}
