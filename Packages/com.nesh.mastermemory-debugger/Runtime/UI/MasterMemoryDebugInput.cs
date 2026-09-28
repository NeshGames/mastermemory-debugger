using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    internal interface IMasterMemoryDebugInputProvider
    {
        int GetTouchCount();
        bool WasPressed(KeyCode keyCode);
    }

    /// <summary>
    /// Input abstraction used by the debugger hotkey. The UI assembly has no hard dependency on the optional Input System;
    /// an adapter assembly registers a provider when that package/input backend is available.
    /// </summary>
    internal static class MasterMemoryDebugInput
    {
        static IMasterMemoryDebugInputProvider s_provider;

        internal static void RegisterProvider(IMasterMemoryDebugInputProvider provider)
        {
            s_provider = provider;
        }

        public static int GetTouchCount()
        {
            if (s_provider != null) return s_provider.GetTouchCount();
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.touchCount;
#else
            return 0;
#endif
        }

        public static bool WasPressed(KeyCode keyCode)
        {
            if (keyCode == KeyCode.None) return false;
            if (s_provider != null) return s_provider.WasPressed(keyCode);
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(keyCode);
#else
            return false;
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => s_provider = null;
    }
}
