using System;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Opens / closes the UI Toolkit runtime debugger.
    /// The UI is created lazily on <see cref="Open"/> and destroyed on <see cref="Close"/>;
    /// nothing is updated while it is closed.
    /// </summary>
    public static class RuntimeMasterMemoryDebugger
    {
        static MasterMemoryDebuggerDocument s_document;

        /// <summary>Raised with the new state after the debugger was opened or closed.</summary>
        public static event Action<bool> OpenStateChanged;

        public static bool IsOpen => s_document != null;

        /// <summary>True when the debugger can be opened in this build / with the current settings.</summary>
        public static bool IsAvailable => MasterMemoryDebugBuild.IsEnabled && MasterMemoryDebuggerSettings.Current.Enabled && Application.isPlaying;

        public static bool Open()
        {
            if (IsOpen) return true;
            if (!IsAvailable)
            {
                if (MasterMemoryDebugBuild.IsEnabled && !Application.isPlaying)
                {
                    MasterMemoryDebugLog.Warning("The runtime debugger can only be opened in Play Mode.");
                }
                return false;
            }

            try
            {
                s_document = MasterMemoryDebuggerDocument.Create();
            }
            catch (MissingReferenceException e)
            {
                MasterMemoryDebugLog.Warning(e.Message);
                s_document = null;
                return false;
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Error("Failed to open the runtime debugger: " + e);
                s_document = null;
                return false;
            }
            OpenStateChanged?.Invoke(true);
            return true;
        }

        public static void Close()
        {
            if (s_document == null) return;
            var document = s_document;
            s_document = null;
            if (document != null) UnityEngine.Object.Destroy(document.gameObject);
            OpenStateChanged?.Invoke(false);
        }

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>Called when the host GameObject is destroyed by something else (for example a scene unload).</summary>
        internal static void NotifyDestroyed(MasterMemoryDebuggerDocument document)
        {
            if (s_document != document) return;
            s_document = null;
            OpenStateChanged?.Invoke(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_document = null;
            OpenStateChanged = null;
        }
    }
}
