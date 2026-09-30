using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Header identity, override count and remote-connection presentation.</summary>
    internal sealed class MasterDebuggerHeaderController
    {
        readonly Label versionLabel;
        readonly Label overrideCountLabel;
        readonly Button remoteButton;

        public MasterDebuggerHeaderController(VisualElement root)
        {
            versionLabel = Required<Label>(root, "mm-master-version");
            overrideCountLabel = Required<Label>(root, "mm-override-count");
            remoteButton = Required<Button>(root, "mm-remote");
        }

        public void Refresh(int overrideCount)
        {
            versionLabel.text = "Master: " + MasterMemoryDebugRegistry.GetMasterVersion();
            versionLabel.tooltip = versionLabel.text;
            overrideCountLabel.text = overrideCount > 0 ? $"{overrideCount} overrides" : "No overrides";
            overrideCountLabel.EnableInClassList("mm-debugger__override-count--active", overrideCount > 0);
        }

        public void RefreshRemote()
        {
            var state = MasterMemoryDebugRemote.State;
            remoteButton.text = state == MasterMemoryRemoteState.Connected ? "Remote ●"
                : state == MasterMemoryRemoteState.Listening ? "Remote …"
                : state == MasterMemoryRemoteState.Connecting ? "Remote …"
                : "Remote";
            remoteButton.tooltip = MasterMemoryDebugRemote.Status;
            remoteButton.EnableInClassList("mm-debugger__remote--connected", state == MasterMemoryRemoteState.Connected);
            remoteButton.EnableInClassList("mm-debugger__remote--failed", state == MasterMemoryRemoteState.Failed);
        }

        static T Required<T>(VisualElement root, string name) where T : VisualElement =>
            root.Q<T>(name) ?? throw new System.InvalidOperationException(
                $"MasterMemoryDebugger.uxml is missing <{typeof(T).Name} name=\"{name}\">.");
    }
}
