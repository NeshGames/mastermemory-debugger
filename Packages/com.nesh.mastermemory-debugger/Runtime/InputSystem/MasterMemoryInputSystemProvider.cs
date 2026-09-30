#if MMDEBUGGER_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Optional Unity Input System backend. Compiled only when com.unity.inputsystem is available and enabled.</summary>
    internal sealed class MasterMemoryInputSystemProvider : IMasterMemoryDebugInputProvider
    {
        KeyCode cachedKeyCode = KeyCode.None;
        Key cachedKey = Key.None;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            MasterMemoryDebugInput.RegisterProvider(new MasterMemoryInputSystemProvider());
        }

        public int GetTouchCount()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null) return 0;
            var count = 0;
            foreach (var touch in touchscreen.touches)
            {
                if (touch.press.isPressed) count++;
            }
            return count;
        }

        public bool WasPressed(KeyCode keyCode)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || keyCode == KeyCode.None) return false;
            if (keyCode != cachedKeyCode)
            {
                cachedKeyCode = keyCode;
                cachedKey = ToInputSystemKey(keyCode);
            }
            return cachedKey != Key.None && keyboard[cachedKey].wasPressedThisFrame;
        }

        static Key ToInputSystemKey(KeyCode keyCode)
        {
            if (keyCode >= KeyCode.Alpha0 && keyCode <= KeyCode.Alpha9)
                return Key.Digit0 + (keyCode - KeyCode.Alpha0);
            if (keyCode >= KeyCode.Keypad0 && keyCode <= KeyCode.Keypad9)
                return Key.Numpad0 + (keyCode - KeyCode.Keypad0);
            switch (keyCode)
            {
                case KeyCode.BackQuote: return Key.Backquote;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.KeypadEnter: return Key.NumpadEnter;
                case KeyCode.Print: return Key.PrintScreen;
            }
            return System.Enum.TryParse(keyCode.ToString(), out Key key) ? key : Key.None;
        }
    }
}
#endif
