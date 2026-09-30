using Nesh.MasterMemoryDebugger;

namespace Harness.Consumer
{
    internal static class NoInputSystemConsumer
    {
        public static bool CanReferenceDebugger() =>
            MasterMemoryDebugRuntime.IsEnabled || RuntimeMasterMemoryDebugger.IsOpen;
    }
}
