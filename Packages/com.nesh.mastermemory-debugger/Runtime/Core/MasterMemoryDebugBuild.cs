using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Switch for every debugger feature.
    /// Enabled in the Unity Editor and in Development Builds (<see cref="Debug.isDebugBuild"/>).
    /// Define <c>MMDEBUGGER_DISABLE</c> to turn the debugger off even in those builds.
    /// </summary>
    /// <remarks>
    /// The DEVELOPMENT_BUILD scripting define is deprecated since Unity 6.6, so players read
    /// <see cref="Debug.isDebugBuild"/> once on the main thread before any scene is loaded.
    /// </remarks>
    public static class MasterMemoryDebugBuild
    {
#if MMDEBUGGER_DISABLE
        public static bool IsEnabled => false;
#elif UNITY_EDITOR
        public static bool IsEnabled => true;
#else
        static bool s_enabled;

        public static bool IsEnabled => s_enabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Initialize()
        {
            s_enabled = Debug.isDebugBuild;
        }
#endif
    }
}
