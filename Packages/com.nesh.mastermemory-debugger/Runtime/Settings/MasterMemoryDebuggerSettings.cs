using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Project wide settings of the runtime debugger.
    /// The asset is loaded from <c>Resources/MasterMemoryDebuggerSettings</c>;
    /// when it does not exist the default values are used.
    /// Edit it from Project Settings &gt; MasterMemory Debugger.
    /// </summary>
    public sealed class MasterMemoryDebuggerSettings : ScriptableObject
    {
        public const string ResourcesPath = "MasterMemoryDebuggerSettings";

        [Tooltip("Enables the runtime debugger UI, the toggle hotkey and patch auto loading.")]
        [SerializeField] bool enabled = true;

        [Tooltip("Allows editing non-key fields. When disabled the debugger is a read-only browser.")]
        [SerializeField] bool allowEditing = true;

        [Tooltip("Allows saving / exporting patches.")]
        [SerializeField] bool allowPatchSave = true;

        [Tooltip("Loads the saved patch when tables get registered.")]
        [SerializeField] bool autoLoadPatch = false;

        [Tooltip("Keyboard key that toggles the debugger.")]
        [SerializeField] KeyCode toggleKey = KeyCode.F8;

        [Tooltip("Maximum number of records shown in the record list.")]
        [SerializeField, Min(1)] int maxSearchResults = 500;

        [Tooltip("Shows secondary key fields in the inspector (always read-only).")]
        [SerializeField] bool showSecondaryKeys = true;

        [SerializeField] MasterMemoryDebugLogLevel logLevel = MasterMemoryDebugLogLevel.Warning;

        [Tooltip("File name of the patch saved under Application.persistentDataPath/MasterMemoryDebugger/.")]
        [SerializeField] string patchFileName = "debug.patch.json";

        [Tooltip("Optional PanelSettings. When empty the debugger creates its own panel.")]
        [SerializeField] PanelSettings panelSettings;

        [Tooltip("Sorting order of the panel created by the debugger. Higher is drawn on top.")]
        [SerializeField] int sortingOrder = 10000;

        public bool Enabled { get => enabled; set => enabled = value; }
        public bool AllowEditing { get => allowEditing; set => allowEditing = value; }
        public bool AllowPatchSave { get => allowPatchSave; set => allowPatchSave = value; }
        public bool AutoLoadPatch { get => autoLoadPatch; set => autoLoadPatch = value; }
        public KeyCode ToggleKey { get => toggleKey; set => toggleKey = value; }
        public int MaxSearchResults { get => maxSearchResults; set => maxSearchResults = Mathf.Max(1, value); }
        public bool ShowSecondaryKeys { get => showSecondaryKeys; set => showSecondaryKeys = value; }
        public MasterMemoryDebugLogLevel LogLevel { get => logLevel; set => logLevel = value; }
        public string PatchFileName { get => string.IsNullOrEmpty(patchFileName) ? "debug.patch.json" : patchFileName; set => patchFileName = value; }
        public PanelSettings PanelSettings { get => panelSettings; set => panelSettings = value; }
        public int SortingOrder { get => sortingOrder; set => sortingOrder = value; }

        static MasterMemoryDebuggerSettings s_current;

        /// <summary>The active settings. Loaded lazily from Resources.</summary>
        public static MasterMemoryDebuggerSettings Current
        {
            get
            {
                if (s_current == null)
                {
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
