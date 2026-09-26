using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Tables pinned as tabs above the record grid, in pin order. Saved in PlayerPrefs (per device).</summary>
    internal static class MasterTablePins
    {
        const string PrefsKey = "Nesh.MasterMemoryDebugger.PinnedTables";

        static List<string> s_names;
        static bool s_persist = true;

        public static event Action Changed;

        public static IReadOnlyList<string> Names => Load();

        public static bool IsPinned(string tableName) => tableName != null && Load().Contains(tableName);

        public static void Pin(string tableName)
        {
            if (string.IsNullOrEmpty(tableName) || IsPinned(tableName)) return;
            Load().Add(tableName);
            Save();
        }

        public static void Unpin(string tableName)
        {
            if (!Load().Remove(tableName)) return;
            Save();
        }

        /// <summary>Tests: an empty list that is never written to PlayerPrefs.</summary>
        internal static void ResetForTests()
        {
            s_names = new List<string>();
            s_persist = false;
        }

        internal static void EndTests()
        {
            s_names = null;
            s_persist = true;
        }

        static List<string> Load()
        {
            if (s_names != null) return s_names;
            s_names = new List<string>();
            if (!s_persist) return s_names;
            try
            {
                var text = PlayerPrefs.GetString(PrefsKey, string.Empty);
                foreach (var name in text.Split('\n'))
                {
                    if (name.Length > 0 && !s_names.Contains(name)) s_names.Add(name);
                }
            }
            catch (Exception)
            {
                // PlayerPrefs unavailable (e.g. called off the main thread): start empty
            }
            return s_names;
        }

        static void Save()
        {
            if (!s_persist)
            {
                Changed?.Invoke();
                return;
            }
            try
            {
                PlayerPrefs.SetString(PrefsKey, string.Join("\n", s_names));
                PlayerPrefs.Save();
            }
            catch (Exception)
            {
                // keep the in-memory list
            }
            Changed?.Invoke();
        }
    }
}
