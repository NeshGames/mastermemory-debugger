using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// The only object that exists while the debugger is closed: listens for the toggle key (F8 by default).
    /// Supports the Input System package and the legacy Input Manager.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class MasterMemoryDebuggerHotkey : MonoBehaviour
    {
        static MasterMemoryDebuggerHotkey s_instance;

#if MMDEBUGGER_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        KeyCode cachedKeyCode = KeyCode.None;
        UnityEngine.InputSystem.Key cachedKey = UnityEngine.InputSystem.Key.None;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            if (!MasterMemoryDebugBuild.IsEnabled || s_instance != null) return;
            var settings = MasterMemoryDebuggerSettings.Current;
            if (!settings.Enabled || settings.ToggleKey == KeyCode.None) return;

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
            if (WasTogglePressed()) RuntimeMasterMemoryDebugger.Toggle();
        }

        bool WasTogglePressed()
        {
            var keyCode = MasterMemoryDebuggerSettings.Current.ToggleKey;
            if (keyCode == KeyCode.None) return false;

#if MMDEBUGGER_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null) return false;
            if (keyCode != cachedKeyCode)
            {
                cachedKeyCode = keyCode;
                cachedKey = ToInputSystemKey(keyCode);
            }
            return cachedKey != UnityEngine.InputSystem.Key.None && keyboard[cachedKey].wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(keyCode);
#else
            return false;
#endif
        }

#if MMDEBUGGER_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        static UnityEngine.InputSystem.Key ToInputSystemKey(KeyCode keyCode)
        {
            if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
            {
                return UnityEngine.InputSystem.Key.Digit0 + (keyCode - KeyCode.Alpha0);
            }
            if (keyCode >= KeyCode.Keypad0 && keyCode <= KeyCode.Keypad9)
            {
                return UnityEngine.InputSystem.Key.Numpad0 + (keyCode - KeyCode.Keypad0);
            }
            switch (keyCode)
            {
                case KeyCode.BackQuote: return UnityEngine.InputSystem.Key.Backquote;
                case KeyCode.Return: return UnityEngine.InputSystem.Key.Enter;
                case KeyCode.KeypadEnter: return UnityEngine.InputSystem.Key.NumpadEnter;
                case KeyCode.Print: return UnityEngine.InputSystem.Key.PrintScreen;
            }
            // F1..F12, A..Z, Space, Tab, Escape, Insert, Delete, Home, End, PageUp, PageDown, Pause, ScrollLock ...
            return System.Enum.TryParse(keyCode.ToString(), out UnityEngine.InputSystem.Key key) ? key : UnityEngine.InputSystem.Key.None;
        }
#endif
    }
}
