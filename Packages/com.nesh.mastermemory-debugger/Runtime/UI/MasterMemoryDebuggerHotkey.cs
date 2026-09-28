using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// The only object that exists while the debugger is closed: listens for the toggle key (F8 by default)
    /// and, on touch screens, for several fingers held down (3 fingers for 1 second by default).
    /// Supports the Input System package and the legacy Input Manager.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class MasterMemoryDebuggerHotkey : MonoBehaviour
    {
        static MasterMemoryDebuggerHotkey s_instance;

        readonly MasterMemoryTouchGesture touchGesture = new MasterMemoryTouchGesture();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            if (!MasterMemoryDebugBuild.IsEnabled || s_instance != null) return;
            var settings = MasterMemoryDebuggerSettings.Current;
            if (!settings.Enabled || (settings.ToggleKey == KeyCode.None && settings.TouchToggleFingers == 0)) return;
            // a build without the UI (remote server only) has nothing to toggle
            if (!MasterMemoryDebuggerAssets.IsUIIncluded) return;

            var gameObject = new GameObject("MasterMemoryDebuggerHotkey");
            gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            DontDestroyOnLoad(gameObject);
            s_instance = gameObject.AddComponent<MasterMemoryDebuggerHotkey>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_instance = null;
        }

        void Update()
        {
            var settings = MasterMemoryDebuggerSettings.Current;
            var touchToggle = touchGesture.Update(GetTouchCount(), settings.TouchToggleFingers, settings.TouchToggleSeconds, Time.unscaledTime);
            // the remote editor tool is the debugger: it is never closed
            if ((WasTogglePressed() || touchToggle) && !MasterMemoryDebugRemote.IsToolMode) RuntimeMasterMemoryDebugger.Toggle();
        }

        static int GetTouchCount() => MasterMemoryDebugInput.GetTouchCount();

        bool WasTogglePressed() =>
            MasterMemoryDebugInput.WasPressed(MasterMemoryDebuggerSettings.Current.ToggleKey);
    }
}
