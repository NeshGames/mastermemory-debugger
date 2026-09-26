namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Compile-time switch for every debugger feature.
    /// Enabled only in the Unity Editor and in Development Builds.
    /// Define <c>MMDEBUGGER_DISABLE</c> to turn the debugger off even in those builds.
    /// </summary>
    public static class MasterMemoryDebugBuild
    {
#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !MMDEBUGGER_DISABLE
        public static readonly bool IsEnabled = true;
#else
        public static readonly bool IsEnabled = false;
#endif
    }
}
