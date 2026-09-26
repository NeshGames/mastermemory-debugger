using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Recent searches, newest first, offered by the search box. Saved in PlayerPrefs (per device).</summary>
    internal static class MasterSearchHistory
    {
        public const int MaxEntries = 10;
        const string PrefsKey = "Nesh.MasterMemoryDebugger.SearchHistory";

        static List<string> s_entries;
        static bool s_persist = true;

        public static IReadOnlyList<string> Entries => Load();

        /// <summary>Moves <paramref name="query"/> to the top (empty queries are ignored).</summary>
        public static void Add(string query)
        {
            query = query?.Trim();
            if (string.IsNullOrEmpty(query) || query.IndexOf('\n') >= 0) return;
            var entries = Load();
            if (entries.Count > 0 && entries[0] == query) return;
            entries.Remove(query);
            entries.Insert(0, query);
            if (entries.Count > MaxEntries) entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
            Save();
        }

        /// <summary>Entries that contain <paramref name="text"/> (all when empty), newest first.</summary>
        public static List<string> Find(string text)
        {
            var result = new List<string>();
            foreach (var entry in Load())
            {
                if (string.IsNullOrEmpty(text) || (entry != text && entry.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)) result.Add(entry);
            }
            return result;
        }

        /// <summary>Tests: an empty history that is never written to PlayerPrefs.</summary>
        internal static void ResetForTests()
        {
            s_entries = new List<string>();
            s_persist = false;
        }

        internal static void EndTests()
        {
            s_entries = null;
            s_persist = true;
        }

        static List<string> Load()
        {
            if (s_entries != null) return s_entries;
            s_entries = new List<string>();
            if (!s_persist) return s_entries;
            try
            {
                foreach (var entry in PlayerPrefs.GetString(PrefsKey, string.Empty).Split('\n'))
                {
                    if (entry.Length > 0 && !s_entries.Contains(entry) && s_entries.Count < MaxEntries) s_entries.Add(entry);
                }
            }
            catch (Exception)
            {
                // PlayerPrefs unavailable: start empty
            }
            return s_entries;
        }

        static void Save()
        {
            if (!s_persist) return;
            try
            {
                PlayerPrefs.SetString(PrefsKey, string.Join("\n", s_entries));
                PlayerPrefs.Save();
            }
            catch (Exception)
            {
                // kept for this session
            }
        }
    }
}
