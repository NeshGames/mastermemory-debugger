using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Host of the debugger UI:
    /// <code>
    /// MasterMemoryRuntimeDebugger (GameObject, DontDestroyOnLoad)
    /// ├─ UIDocument
    /// └─ MasterMemoryDebuggerDocument
    /// </code>
    /// </summary>
    [AddComponentMenu("")]
    public sealed class MasterMemoryDebuggerDocument : MonoBehaviour
    {
        public const string GameObjectName = "MasterMemoryRuntimeDebugger";

        UIDocument document;
        PanelSettings ownedPanelSettings;
        MasterMemoryDebuggerController controller;

        public UIDocument Document => document;

        /// <summary>PanelSettings created by the debugger; null when the project supplied its own.</summary>
        internal PanelSettings OwnedPanelSettings => ownedPanelSettings;

        internal static MasterMemoryDebuggerDocument Create()
        {
            var layout = MasterMemoryDebuggerAssets.Layout;
            if (layout == null)
            {
                throw new MissingReferenceException("The debugger UI is not included in this build " +
                                                    "(Project Settings > MasterMemory Debugger > Include Debugger UI).");
            }

            var settings = MasterMemoryDebuggerSettings.Current;
            var gameObject = new GameObject(GameObjectName);
            gameObject.SetActive(false);
            DontDestroyOnLoad(gameObject);

            var host = gameObject.AddComponent<MasterMemoryDebuggerDocument>();
            host.document = gameObject.AddComponent<UIDocument>();
            if (settings.PanelSettings != null)
            {
                host.document.panelSettings = settings.PanelSettings;
            }
            else
            {
                host.ownedPanelSettings = CreatePanelSettings(settings);
                host.document.panelSettings = host.ownedPanelSettings;
            }
            host.document.visualTreeAsset = layout;

            gameObject.SetActive(true);
            return host;
        }

        static PanelSettings CreatePanelSettings(MasterMemoryDebuggerSettings settings)
        {
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "MasterMemoryDebuggerPanelSettings";
            panelSettings.hideFlags = HideFlags.DontSave;
            panelSettings.themeStyleSheet = MasterMemoryDebuggerAssets.Theme;
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1600, 900);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            panelSettings.sortingOrder = settings.SortingOrder;
            panelSettings.clearColor = false;
            return panelSettings;
        }

        void Start()
        {
            // UIDocument builds its visual tree in its own OnEnable; binding in Start guarantees the tree exists.
            var root = document.rootVisualElement;
            // Popup menus (EnumField / DropdownField) are added to the panel root, outside the document.
            // When the panel belongs to the debugger, style the whole panel so the menus get the dark theme and the font;
            // a panel shared with the game is left untouched.
            var styleRoot = ownedPanelSettings != null && root.panel != null ? root.panel.visualTree : root;
            var style = MasterMemoryDebuggerAssets.Style;
            if (style != null) styleRoot.styleSheets.Add(style);
            var font = MasterMemoryDebuggerSettings.Current.Font;
            if (font != null) styleRoot.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            controller = new MasterMemoryDebuggerController(root, this);
        }

        void OnDestroy()
        {
            controller?.Dispose();
            controller = null;
            if (ownedPanelSettings != null) Destroy(ownedPanelSettings);
            RuntimeMasterMemoryDebugger.NotifyDestroyed(this);
        }
    }
}
