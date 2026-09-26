// Compile-only stubs mirroring the Unity 6000.0 API surface used by the package.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public struct Rect { public float xMin { get; set; } public float xMax { get; set; } public float yMax { get; set; } public float width { get; set; } public float height { get; set; } }
    public class Object
    {
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public static void Destroy(Object obj) { }
        public static void DestroyImmediate(Object obj) { }
        public static void DontDestroyOnLoad(Object target) { }
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !ReferenceEquals(a, b);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => base.GetHashCode();
        public static implicit operator bool(Object o) => !ReferenceEquals(o, null);
    }
    [Flags] public enum HideFlags { None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8, DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61 }
    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject => Activator.CreateInstance<T>();
    }
    public class Component : Object { public GameObject gameObject => null; }
    public class Behaviour : Component { public bool enabled { get; set; } }
    public class MonoBehaviour : Behaviour { }
    public sealed class GameObject : Object
    {
        public GameObject(string name) { this.name = name; }
        public void SetActive(bool value) { }
        public bool activeSelf => true;
        public T AddComponent<T>() where T : Component => Activator.CreateInstance<T>();
        public T GetComponent<T>() => default;
        public static GameObject Find(string name) => null;
    }
    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => null;
    }
    public static class Debug
    {
        public static void Log(object message) => Console.WriteLine(message);
        public static void LogWarning(object message) => Console.WriteLine("WARN " + message);
        public static void LogError(object message) => Console.WriteLine("ERROR " + message);
        public static void LogException(Exception e) => Console.WriteLine("EXC " + e);
        public static bool isDebugBuild => true;
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Clamp(int v, int min, int max) => Math.Min(Math.Max(v, min), max);
        public static float Clamp(float v, float min, float max) => Math.Min(Math.Max(v, min), max);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Sign(float v) => v >= 0f ? 1f : -1f;
        public static float Abs(float v) => Math.Abs(v);
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-6f;
    }
    public enum RuntimePlatform { OSXEditor = 0, OSXPlayer = 1, WindowsPlayer = 2, WindowsEditor = 7, IPhonePlayer = 8, Android = 11, LinuxPlayer = 13, LinuxEditor = 16, WebGLPlayer = 17 }
    public static class Application
    {
        public static string persistentDataPath => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mmdebugger-harness");
        public static RuntimePlatform platform => RuntimePlatform.LinuxPlayer;
        public static bool isPlaying => true;
        public static bool isEditor => true;
        public static void OpenURL(string url) { }
    }
    public enum KeyCode { UpArrow = 273, DownArrow = 274, None = 0, Backspace = 8, Tab = 9, Return = 13, Escape = 27, Space = 32, BackQuote = 96, Alpha0 = 48, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9, A = 97, Keypad0 = 256, Keypad1, Keypad2, Keypad3, Keypad4, Keypad5, Keypad6, Keypad7, Keypad8, Keypad9, KeypadEnter = 271, F1 = 282, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, Print = 316 }
    public static class Input { public static bool GetKeyDown(KeyCode key) => false; public static int touchCount => 0; }
    public enum RuntimeInitializeLoadType { AfterSceneLoad = 0, BeforeSceneLoad = 1, AfterAssembliesLoaded = 2, BeforeSplashScreen = 3, SubsystemRegistration = 4 }
    [AttributeUsage(AttributeTargets.Method)] public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    public class PropertyAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class TooltipAttribute : PropertyAttribute { public TooltipAttribute(string tooltip) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute : PropertyAttribute { public MinAttribute(float min) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : PropertyAttribute { public RangeAttribute(float min, float max) { } }
    public static class Time { public static float unscaledTime => 0f; }
    public class Font : Object { }
    public static class PlayerPrefs
    {
        static readonly System.Collections.Generic.Dictionary<string, string> s_values = new System.Collections.Generic.Dictionary<string, string>();
        public static string GetString(string key, string defaultValue) => s_values.TryGetValue(key, out var v) ? v : defaultValue;
        public static void SetString(string key, string value) => s_values[key] = value;
        public static void DeleteKey(string key) => s_values.Remove(key);
        public static void Save() { }
    }
    [AttributeUsage(AttributeTargets.Class)] public sealed class AddComponentMenu : Attribute { public AddComponentMenu(string menuName) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HeaderAttribute : PropertyAttribute { public HeaderAttribute(string header) { } }
    public class MissingReferenceException : Exception { public MissingReferenceException(string m) : base(m) { } }
    public struct Vector2 : IEquatable<Vector2> { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } public bool Equals(Vector2 o) => x == o.x && y == o.y; public override bool Equals(object o) => o is Vector2 v && Equals(v); public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode(); }
    public struct Vector3 : IEquatable<Vector3> { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } public bool Equals(Vector3 o) => x == o.x && y == o.y && z == o.z; public override bool Equals(object o) => o is Vector3 v && Equals(v); public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode(); }
    public struct Vector2Int : IEquatable<Vector2Int> { public int x { get; set; } public int y { get; set; } public Vector2Int(int x, int y) { this.x = x; this.y = y; } public bool Equals(Vector2Int o) => x == o.x && y == o.y; public override bool Equals(object o) => o is Vector2Int v && Equals(v); public override int GetHashCode() => x ^ y; }
    public struct Vector3Int : IEquatable<Vector3Int> { public int x { get; set; } public int y { get; set; } public int z { get; set; } public Vector3Int(int x, int y, int z) { this.x = x; this.y = y; this.z = z; } public bool Equals(Vector3Int o) => x == o.x && y == o.y && z == o.z; public override bool Equals(object o) => o is Vector3Int v && Equals(v); public override int GetHashCode() => x ^ y ^ z; }
    public struct Color : IEquatable<Color>
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public float this[int index] { get => index == 0 ? r : index == 1 ? g : index == 2 ? b : a; set { if (index == 0) r = value; else if (index == 1) g = value; else if (index == 2) b = value; else a = value; } }
        public bool Equals(Color o) => r == o.r && g == o.g && b == o.b && a == o.a;
        public override bool Equals(object o) => o is Color c && Equals(c);
        public override int GetHashCode() => r.GetHashCode();
    }
}
namespace UnityEngine.InputSystem
{
    public enum Key { None = 0, Space, Enter, Tab, Backquote, Quote, Semicolon, Comma, Period, Slash, Backslash, LeftBracket, RightBracket, Minus, Equals, A, B, C, Digit1 = 41, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0, Escape = 60, F1 = 94, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, NumpadEnter = 77, Numpad0 = 84, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6, Numpad7, Numpad8, Numpad9, PrintScreen = 108 }
    public class KeyControl { public bool wasPressedThisFrame => false; }
    public class Keyboard { public static Keyboard current => null; public KeyControl this[Key key] => null; }
    public class ButtonControl { public bool isPressed => false; }
    public class TouchControl { public ButtonControl press => null; }
    public class Touchscreen { public static Touchscreen current => null; public TouchControl[] touches => new TouchControl[0]; }
}
namespace UnityEngine.TestTools
{
    [System.AttributeUsage(System.AttributeTargets.Method)] public class UnityTestAttribute : System.Attribute { }
}
namespace UnityEngine
{
    public static class GameObjectFindExt { }
}
namespace UnityEngine.Serialization
{
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = true)]
    public class FormerlySerializedAsAttribute : System.Attribute { public FormerlySerializedAsAttribute(string oldName) { } }
}
namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)] public class PreserveAttribute : System.Attribute { }
}
