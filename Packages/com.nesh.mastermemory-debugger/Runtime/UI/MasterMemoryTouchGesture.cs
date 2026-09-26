namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Several fingers held on the screen for a while (toggles the debugger on touch devices).</summary>
    internal sealed class MasterMemoryTouchGesture
    {
        float startTime = -1f;
        bool triggered;

        /// <summary>
        /// True once when <paramref name="fingers"/> or more touches have stayed down for <paramref name="seconds"/>;
        /// the fingers must be lifted before the gesture can trigger again. <paramref name="fingers"/> 0 disables it.
        /// </summary>
        public bool Update(int touchCount, int fingers, float seconds, float time)
        {
            if (fingers <= 0 || touchCount < fingers)
            {
                startTime = -1f;
                triggered = false;
                return false;
            }
            if (triggered) return false;
            if (startTime < 0f) startTime = time;
            if (time - startTime < seconds) return false;
            triggered = true;
            return true;
        }
    }
}
