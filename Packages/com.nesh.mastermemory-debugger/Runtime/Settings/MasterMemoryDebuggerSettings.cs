using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Project wide settings of the runtime debugger. Edit it from Project Settings &gt; MasterMemory Debugger.
    /// The asset is kept outside Resources; the Editor finds it anywhere, and the build processor of the package copies it
    /// into a temporary Resources folder (<c>Resources/MasterMemoryDebuggerSettings</c>) only for Development Builds, so
    /// release builds contain neither the asset nor what it references (font, PanelSettings).
    /// When it does not exist the default values are used.
    /// </summary>
    public sealed class MasterMemoryDebuggerSettings : ScriptableObject
    {
        public const string ResourcesPath = "MasterMemoryDebuggerSettings";

        [Tooltip("Enables the runtime debugger UI, the toggle hotkey and patch auto loading.")]
        [SerializeField] bool enabled = true;

        [Tooltip("Development Builds: include the debugger UI (layout, style sheets, font). Turn off for builds that only " +
                 "run the remote server for the remote editor tool; the debugger can not be opened in them. The Editor always has the UI.")]
        [SerializeField] bool includeDebuggerUI = true;

        [Tooltip("Allows editing non-key fields. When disabled the debugger is a read-only browser.")]
        [SerializeField] bool allowEditing = true;

        [Tooltip("Allows saving / exporting patches.")]
        [SerializeField] bool allowPatchSave = true;

        [Tooltip("Loads the saved patch when tables get registered.")]
        [SerializeField] bool autoLoadPatch = false;

        [Tooltip("Keyboard key that toggles the debugger.")]
        [SerializeField] KeyCode toggleKey = KeyCode.F8;

        [Tooltip("Touch devices: number of fingers held on the screen to toggle the debugger. 0 disables the gesture.")]
        [SerializeField, Range(0, 5)] int touchToggleFingers = 3;

        [Tooltip("Touch devices: seconds the fingers must stay on the screen.")]
        [SerializeField, Min(0.1f)] float touchToggleSeconds = 1f;

        [Tooltip("Maximum number of records shown in the record list.")]
        [SerializeField, Min(1)] int maxSearchResults = 500;

        [Tooltip("Shows secondary key fields in the inspector (always read-only).")]
        [SerializeField] bool showSecondaryKeys = true;

        [SerializeField] MasterMemoryDebugLogLevel logLevel = MasterMemoryDebugLogLevel.Warning;

        [Tooltip("Logs the changed fields with old → new values when overrides are applied, reset or loaded (colored in the Editor Console).")]
        [SerializeField] bool logOverrideChanges = true;

        [Tooltip("Patch selected by default and used by Auto Load Patch. Saved as <name>.patch.json under Application.persistentDataPath/MasterMemoryDebugger/.")]
        [SerializeField, FormerlySerializedAs("patchFileName")] string defaultPatchName = "debug";

        [Tooltip("Optional font of the debugger UI (TTF / OTF). Use a font with the glyphs of your labels, e.g. CJK for Chinese / Japanese table and field names.")]
        [SerializeField] Font font;

        [Tooltip("Optional PanelSettings. When empty the debugger creates its own panel.")]
        [SerializeField] PanelSettings panelSettings;

        [Tooltip("Sorting order of the panel created by the debugger. Higher is drawn on top.")]
        [SerializeField] int sortingOrder = 10000;

        [Header("Remote editing")]
        [Tooltip("Development Builds: listen for the remote editor tool at startup (not on WebGL). Also started from the Remote dialog or MasterMemoryDebugRemote.StartServer().")]
        [SerializeField] bool remoteServer = false;

        [Tooltip("TCP port the game listens on for the remote editor tool.")]
        [SerializeField, Range(1024, 65535)] int remotePort = MasterMemoryDebugDefaults.RemotePort;

        [Tooltip("Code the tool must enter. Empty = a random 6 digit code at every start (shown in the Remote dialog and the log).")]
        [SerializeField] string remotePairingCode = "";

        public bool Enabled { get => enabled; set => enabled = value; }
        public bool IncludeDebuggerUI { get => includeDebuggerUI; set => includeDebuggerUI = value; }
        public bool AllowEditing { get => allowEditing; set => allowEditing = value; }
        public bool AllowPatchSave { get => allowPatchSave; set => allowPatchSave = value; }
        public bool AutoLoadPatch { get => autoLoadPatch; set => autoLoadPatch = value; }
        public KeyCode ToggleKey { get => toggleKey; set => toggleKey = value; }
        public int TouchToggleFingers { get => touchToggleFingers; set => touchToggleFingers = Mathf.Clamp(value, 0, 5); }
        public float TouchToggleSeconds { get => touchToggleSeconds; set => touchToggleSeconds = Mathf.Max(0.1f, value); }
        public int MaxSearchResults { get => maxSearchResults; set => maxSearchResults = Mathf.Max(1, value); }
        public bool ShowSecondaryKeys { get => showSecondaryKeys; set => showSecondaryKeys = value; }
        public MasterMemoryDebugLogLevel LogLevel { get => logLevel; set => logLevel = value; }
        public bool LogOverrideChanges { get => logOverrideChanges; set => logOverrideChanges = value; }
        public string DefaultPatchName { get => string.IsNullOrEmpty(defaultPatchName) ? "debug" : defaultPatchName; set => defaultPatchName = value; }
        public PanelSettings PanelSettings { get => panelSettings; set => panelSettings = value; }
        public Font Font { get => font; set => font = value; }
        public int SortingOrder { get => sortingOrder; set => sortingOrder = value; }
        public bool RemoteServer { get => remoteServer; set => remoteServer = value; }
        public int RemotePort { get => remotePort; set => remotePort = Mathf.Clamp(value, 1, 65535); }
        public string RemotePairingCode { get => remotePairingCode; set => remotePairingCode = value; }

        static MasterMemoryDebuggerSettings s_current;

        /// <summary>The active settings. Loaded lazily from Resources.</summary>
        public static MasterMemoryDebuggerSettings Current
        {
            get
            {
                if (s_current == null)
                {
#if UNITY_EDITOR
                    // the asset of the project, wherever it is
                    foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:" + nameof(MasterMemoryDebuggerSettings)))
                    {
                        s_current = UnityEditor.AssetDatabase.LoadAssetAtPath<MasterMemoryDebuggerSettings>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                        if (s_current != null) break;
                    }
                    if (s_current == null)
#endif
                    s_current = Resources.Load<MasterMemoryDebuggerSettings>(ResourcesPath);
                    if (s_current == null)
                    {
                        s_current = CreateInstance<MasterMemoryDebuggerSettings>();
                        s_current.hideFlags = HideFlags.DontSave;
                    }
                }
                return s_current;
            }
        }

        /// <summary>Replaces the active settings at runtime (for example from a project debug menu).</summary>
        public static void SetCurrent(MasterMemoryDebuggerSettings settings)
        {
            s_current = settings;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_current = null;
        }
    }
}
