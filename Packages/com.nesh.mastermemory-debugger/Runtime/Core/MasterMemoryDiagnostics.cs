using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    public sealed class MasterMemoryDiagnosticEntry
    {
        public DateTime TimeUtc;
        public string Category;
        public string Operation;
        public double Milliseconds;
        public string Detail;
    }

    /// <summary>Small in-memory diagnostics ring for development builds. It never persists or ships useful state to release gameplay.</summary>
    public static class MasterMemoryDiagnostics
    {
        public const int MaxEntries = 100;

        static readonly object s_gate = new object();
        static readonly List<MasterMemoryDiagnosticEntry> s_entries = new List<MasterMemoryDiagnosticEntry>();

        public static event Action Changed;

        public static IReadOnlyList<MasterMemoryDiagnosticEntry> Snapshot()
        {
            lock (s_gate) return s_entries.ToArray();
        }

        public static void Record(string category, string operation, double milliseconds, string detail = null)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return;
            lock (s_gate)
            {
                s_entries.Add(new MasterMemoryDiagnosticEntry
                {
                    TimeUtc = DateTime.UtcNow,
                    Category = category ?? string.Empty,
                    Operation = operation ?? string.Empty,
                    Milliseconds = milliseconds,
                    Detail = detail ?? string.Empty,
                });
                if (s_entries.Count > MaxEntries) s_entries.RemoveAt(0);
            }
            Changed?.Invoke();
        }

        public static void Clear()
        {
            lock (s_gate) s_entries.Clear();
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            lock (s_gate) s_entries.Clear();
            Changed = null;
        }
    }
}
