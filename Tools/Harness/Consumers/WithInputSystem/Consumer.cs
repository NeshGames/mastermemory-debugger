using Nesh.MasterMemoryDebugger;
using UnityEngine.InputSystem;

namespace Harness.Consumer
{
    internal static class WithInputSystemConsumer
    {
        public static bool CanReferenceBothAssemblies() =>
            Keyboard.current == null || RuntimeMasterMemoryDebugger.IsOpen;
    }
}
