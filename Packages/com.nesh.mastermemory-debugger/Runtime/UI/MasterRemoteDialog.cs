using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// "Remote" dialog. In a game build: start / stop listening and show the address and pairing code to type in the tool.
    /// In the remote editor tool: the address and code of the game, Connect / Disconnect.
    /// </summary>
    internal static class MasterRemoteDialog
    {
        const string HostPrefsKey = "Nesh.MasterMemoryDebugger.RemoteHost";
        const string PortPrefsKey = "Nesh.MasterMemoryDebugger.RemotePort";

        public static void Show(MasterMemoryDebuggerDialog dialog, Action<string, bool> setStatus)
        {
            if (MasterMemoryDebugRemote.IsToolMode) ShowTool(dialog, setStatus);
            else ShowGame(dialog, setStatus);
        }

        // ------------------------------------------------------------------ tool

        static void ShowTool(MasterMemoryDebuggerDialog dialog, Action<string, bool> setStatus)
        {
            var content = new VisualElement();
            var hostField = new TextField("Game address") { value = ReadPref(HostPrefsKey, "127.0.0.1"), tooltip = "IP address of the device running the game (shown in its Remote dialog). 127.0.0.1 for a game on this PC, or an Android device over USB after adb forward tcp:PORT tcp:PORT." };
            var portField = new TextField("Port") { value = ReadPref(PortPrefsKey, MasterMemoryDebugRemote.DefaultPort.ToString(CultureInfo.InvariantCulture)) };
            var codeField = new TextField("Pairing code") { tooltip = "Shown in the game's Remote dialog and log." };
            content.Add(hostField);
            content.Add(portField);
            content.Add(codeField);
            content.Add(CreateStatusLabel());

            var connected = MasterMemoryDebugRemote.State == MasterMemoryRemoteState.Connected || MasterMemoryDebugRemote.State == MasterMemoryRemoteState.Connecting;
            var cancel = new MasterMemoryDebuggerDialog.DialogButton("Close", null);
            var disconnect = new MasterMemoryDebuggerDialog.DialogButton("Disconnect", () =>
            {
                MasterMemoryDebugRemote.Stop();
                setStatus("Remote: disconnected. The tables shown are the last received.", false);
            }, isDanger: true);
            var connect = new MasterMemoryDebuggerDialog.DialogButton("Connect", () =>
            {
                if (!int.TryParse(portField.value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port <= 0 || port > 65535)
                {
                    setStatus("Remote: enter a port between 1 and 65535.", true);
                    return;
                }
                WritePref(HostPrefsKey, hostField.value?.Trim());
                WritePref(PortPrefsKey, port.ToString(CultureInfo.InvariantCulture));
                MasterMemoryDebugRemote.Connect(hostField.value, port, codeField.value);
                setStatus(MasterMemoryDebugRemote.Status, false);
            }, isPrimary: true);

            dialog.Show(
                "Remote editing",
                "Edit the master data of a running game build (Development Build with remote editing started). Changes are applied to the game immediately, both ways.",
                content,
                connected ? new[] { cancel, disconnect, connect } : new[] { cancel, connect });
            codeField.schedule.Execute(() => codeField.Focus());
        }

        // ------------------------------------------------------------------ game

        static void ShowGame(MasterMemoryDebuggerDialog dialog, Action<string, bool> setStatus)
        {
            var content = new VisualElement();
            if (!MasterMemoryDebugRemote.IsSupported)
            {
                dialog.Show("Remote editing", "Remote editing is not available on this platform (WebGL has no sockets).", new MasterMemoryDebuggerDialog.DialogButton("Close", null));
                return;
            }

            var running = MasterMemoryDebugRemote.IsServerRunning;
            if (running)
            {
                content.Add(CreateInfo("Addresses", string.Join("   ", MasterMemoryDebugRemote.GetLocalAddresses())));
                content.Add(CreateInfo("Port", MasterMemoryDebugRemote.ServerPort.ToString(CultureInfo.InvariantCulture)));
                content.Add(CreateInfo("Pairing code", MasterMemoryDebugRemote.PairingCode, "mm-debugger__remote-code"));
            }
            var settings = MasterMemoryDebuggerSettings.Current;
            var portField = new TextField("Port") { value = settings.RemotePort.ToString(CultureInfo.InvariantCulture) };
            if (!running) content.Add(portField);
            content.Add(CreateStatusLabel());

            var close = new MasterMemoryDebuggerDialog.DialogButton("Close", null);
            if (running)
            {
                dialog.Show(
                    "Remote editing",
                    "Enter the address, port and pairing code in the remote editor tool (a desktop build of this project). Android over USB: run adb forward tcp:PORT tcp:PORT on the PC and connect the tool to 127.0.0.1.",
                    content,
                    close,
                    new MasterMemoryDebuggerDialog.DialogButton("Stop", () =>
                    {
                        MasterMemoryDebugRemote.Stop();
                        setStatus("Remote editing stopped.", false);
                    }, isDanger: true));
                return;
            }

            dialog.Show(
                "Remote editing",
                "Let the remote editor tool (a desktop build of this project) connect to this game and edit its master data. Only in Development Builds; the tool needs the pairing code shown after starting.",
                content,
                close,
                new MasterMemoryDebuggerDialog.DialogButton("Start", () =>
                {
                    if (!int.TryParse(portField.value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port <= 0 || port > 65535)
                    {
                        setStatus("Remote: enter a port between 1 and 65535.", true);
                        return;
                    }
                    if (!MasterMemoryDebugRemote.StartServer(port, settings.RemotePairingCode))
                    {
                        setStatus($"Remote: can not listen on port {port} (see Console).", true);
                        return;
                    }
                    // show the address and code
                    Show(dialog, setStatus);
                }, isPrimary: true));
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The connection status, updated while the dialog is open.</summary>
        static Label CreateStatusLabel()
        {
            var label = new Label(MasterMemoryDebugRemote.Status);
            label.AddToClassList("mm-debugger__hint");
            label.AddToClassList("mm-debugger__remote-status");
            void Refresh() => label.text = MasterMemoryDebugRemote.Status;
            label.RegisterCallback<AttachToPanelEvent>(_ => MasterMemoryDebugRemote.Changed += Refresh);
            label.RegisterCallback<DetachFromPanelEvent>(_ => MasterMemoryDebugRemote.Changed -= Refresh);
            return label;
        }

        static VisualElement CreateInfo(string name, string value, string valueClass = null)
        {
            var row = new VisualElement();
            row.AddToClassList("mm-debugger__remote-info");
            var nameLabel = new Label(name);
            nameLabel.AddToClassList("mm-debugger__remote-info-name");
            var valueLabel = new Label(value);
            valueLabel.AddToClassList("mm-debugger__remote-info-value");
            if (valueClass != null) valueLabel.AddToClassList(valueClass);
            valueLabel.selection.isSelectable = true;
            row.Add(nameLabel);
            row.Add(valueLabel);
            return row;
        }

        static string ReadPref(string key, string fallback)
        {
            try
            {
                return PlayerPrefs.GetString(key, fallback);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        static void WritePref(string key, string value)
        {
            try
            {
                PlayerPrefs.SetString(key, value ?? string.Empty);
                PlayerPrefs.Save();
            }
            catch (Exception)
            {
                // not remembered
            }
        }
    }
}
