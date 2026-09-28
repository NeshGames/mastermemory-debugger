namespace UnityEngine.InputSystem
{
    public enum Key { None = 0, Space, Enter, Tab, Backquote, Quote, Semicolon, Comma, Period, Slash, Backslash, LeftBracket, RightBracket, Minus, Equals, A, B, C, Digit1 = 41, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0, Escape = 60, F1 = 94, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, NumpadEnter = 77, Numpad0 = 84, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6, Numpad7, Numpad8, Numpad9, PrintScreen = 108 }
    public class KeyControl { public bool wasPressedThisFrame => false; }
    public class Keyboard { public static Keyboard current => null; public KeyControl this[Key key] => null; }
    public class ButtonControl { public bool isPressed => false; }
    public class TouchControl { public ButtonControl press => null; }
    public class Touchscreen { public static Touchscreen current => null; public TouchControl[] touches => new TouchControl[0]; }
}
