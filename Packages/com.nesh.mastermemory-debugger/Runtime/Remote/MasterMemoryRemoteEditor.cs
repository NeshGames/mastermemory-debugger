using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Turns a desktop build of the project into the remote editor tool. Put it on a GameObject in an otherwise empty scene,
    /// build that scene alone as a Windows / macOS Development Build, and run it: the debugger opens with a connect
    /// dialog, and every edit is applied to the connected game build.
    /// Nothing else of the game runs in the tool; it only needs the project's record types (same code as the game).
    /// </summary>
    [AddComponentMenu("MasterMemory Debugger/Remote Editor")]
    public sealed class MasterMemoryRemoteEditor : MonoBehaviour
    {
        [Tooltip("Reopens the debugger if something closes it (it fills the window and has no Close button in the tool).")]
        [SerializeField] bool keepOpen = true;

        [Tooltip("Frames per second of the tool; low values save CPU.")]
        [SerializeField, Range(10, 120)] int targetFrameRate = 30;

        void Awake()
        {
            MasterMemoryDebugRemote.IsToolMode = true;
            // stay in sync while another window (the game in the Editor, a terminal) has the focus
            Application.runInBackground = true;
            Application.targetFrameRate = targetFrameRate;
        }

        void Start()
        {
            if (!MasterMemoryDebugRemote.IsSupported)
            {
                Debug.LogError("[MasterMemoryDebugger] The remote editor tool needs the Editor or a Development Build (and is not available on WebGL).");
                return;
            }
            RuntimeMasterMemoryDebugger.Open();
        }

        void Update()
        {
            if (keepOpen && !RuntimeMasterMemoryDebugger.IsOpen && MasterMemoryDebugRemote.IsSupported) RuntimeMasterMemoryDebugger.Open();
        }

        void OnDestroy()
        {
            MasterMemoryDebugRemote.Stop();
            MasterMemoryDebugRemote.IsToolMode = false;
        }
    }
}
