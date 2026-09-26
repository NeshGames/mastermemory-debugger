using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    public enum MasterMemoryDebugLogLevel
    {
        None = 0,
        Error = 1,
        Warning = 2,
        Info = 3,
    }

    internal static class MasterMemoryDebugLog
    {
        const string Prefix = "[MasterMemoryDebugger] ";

        static MasterMemoryDebugLogLevel Level => MasterMemoryDebuggerSettings.Current.LogLevel;

        public static void Info(string message)
        {
            if (Level >= MasterMemoryDebugLogLevel.Info) Debug.Log(Prefix + message);
        }

        public static void Warning(string message)
        {
            if (Level >= MasterMemoryDebugLogLevel.Warning) Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            if (Level >= MasterMemoryDebugLogLevel.Error) Debug.LogError(Prefix + message);
        }
    }
}
