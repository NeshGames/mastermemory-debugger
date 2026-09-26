using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    public enum MasterMemoryDebuggerMessageType
    {
        Info,
        Warning,
        Error,
        Change,
    }

    public readonly struct MasterMemoryDebuggerMessage
    {
        public MasterMemoryDebuggerMessage(DateTime time, MasterMemoryDebuggerMessageType type, string text)
        {
            Time = time;
            Type = type;
            Text = text;
        }

        public DateTime Time { get; }
        public MasterMemoryDebuggerMessageType Type { get; }
        public string Text { get; }
    }

    /// <summary>
    /// The latest debugger messages (status, patch warnings, override changes) for the in-game log panel,
    /// so they can be read on devices without a Console. Kept while the debugger is closed.
    /// </summary>
    public static class MasterMemoryDebuggerMessages
    {
        public const int Capacity = 50;

        static readonly List<MasterMemoryDebuggerMessage> s_messages = new List<MasterMemoryDebuggerMessage>();

        public static event Action Changed;

        /// <summary>Oldest first.</summary>
        public static IReadOnlyList<MasterMemoryDebuggerMessage> Messages => s_messages;

        public static void Add(MasterMemoryDebuggerMessageType type, string text)
        {
            if (!MasterMemoryDebugBuild.IsEnabled || string.IsNullOrEmpty(text)) return;
            if (s_messages.Count >= Capacity) s_messages.RemoveAt(0);
            s_messages.Add(new MasterMemoryDebuggerMessage(DateTime.Now, type, text));
            Changed?.Invoke();
        }

        public static void Clear()
        {
            if (s_messages.Count == 0) return;
            s_messages.Clear();
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_messages.Clear();
            Changed = null;
        }
    }
}
