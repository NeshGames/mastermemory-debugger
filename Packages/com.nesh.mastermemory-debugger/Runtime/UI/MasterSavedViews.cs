using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    internal sealed class MasterSavedView
    {
        public string Name;
        public string TableName;
        public string Query;
        public bool ModifiedOnly;
        public string SortKey;
        public bool SortDescending;
        public string ColumnLayout;

        public MasterRecordViewState ToRecordState() => new MasterRecordViewState
        {
            Query = Query,
            ModifiedOnly = ModifiedOnly,
            SortKey = SortKey,
            SortDescending = SortDescending,
            ColumnLayout = ColumnLayout,
        };

        public MasterSavedView Clone() => new MasterSavedView
        {
            Name = Name,
            TableName = TableName,
            Query = Query,
            ModifiedOnly = ModifiedOnly,
            SortKey = SortKey,
            SortDescending = SortDescending,
            ColumnLayout = ColumnLayout,
        };
    }

    /// <summary>
    /// Named development-only Data views persisted in PlayerPrefs. The JSON format is versioned and unknown/missing
    /// fields degrade to defaults so views survive normal master-schema evolution.
    /// </summary>
    internal static class MasterSavedViews
    {
        internal const int FormatVersion = 1;
        const string PrefsKey = "Nesh.MasterMemoryDebugger.SavedViews";

        static List<MasterSavedView> s_views;
        static bool s_persist = true;

        public static IReadOnlyList<MasterSavedView> Snapshot()
        {
            var source = Load();
            var copy = new List<MasterSavedView>(source.Count);
            foreach (var view in source) copy.Add(view.Clone());
            copy.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return copy;
        }

        public static MasterSavedView Find(string name)
        {
            name = NormalizeName(name);
            if (name == null) return null;
            var found = Load().Find(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            return found?.Clone();
        }

        public static bool Save(MasterSavedView view)
        {
            if (view == null || string.IsNullOrEmpty(view.TableName)) return false;
            var name = NormalizeName(view.Name);
            if (name == null) return false;

            var stored = view.Clone();
            stored.Name = name;
            stored.Query ??= string.Empty;
            stored.ColumnLayout ??= string.Empty;

            var views = Load();
            var index = views.FindIndex(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) views[index] = stored;
            else views.Add(stored);
            Persist();
            return true;
        }

        public static bool Delete(string name)
        {
            name = NormalizeName(name);
            if (name == null) return false;
            var removed = Load().RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) Persist();
            return removed;
        }

        public static string NormalizeName(string name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name.IndexOf('\n') >= 0 || name.IndexOf('\r') >= 0) return null;
            return name.Length > 64 ? name.Substring(0, 64).Trim() : name;
        }

        internal static string Serialize(IReadOnlyList<MasterSavedView> views)
        {
            var items = new List<object>();
            if (views != null)
            {
                foreach (var view in views)
                {
                    if (view == null || NormalizeName(view.Name) == null || string.IsNullOrEmpty(view.TableName)) continue;
                    items.Add(new MasterDataJsonObject
                    {
                        { "name", NormalizeName(view.Name) },
                        { "table", view.TableName },
                        { "query", view.Query ?? string.Empty },
                        { "modifiedOnly", view.ModifiedOnly },
                        { "sortKey", view.SortKey },
                        { "sortDescending", view.SortDescending },
                        { "columns", view.ColumnLayout ?? string.Empty },
                    });
                }
            }
            return MasterDataJson.Serialize(new MasterDataJsonObject
            {
                { "formatVersion", FormatVersion },
                { "views", items },
            }, pretty: false);
        }

        internal static List<MasterSavedView> Deserialize(string json)
        {
            var result = new List<MasterSavedView>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            MasterDataJsonObject root;
            try
            {
                root = MasterDataJson.Parse(json) as MasterDataJsonObject;
            }
            catch (FormatException)
            {
                return result;
            }
            if (root == null || ReadInt(root["formatVersion"]) != FormatVersion
                || !(root["views"] is List<object> items))
                return result;

            foreach (var item in items)
            {
                if (!(item is MasterDataJsonObject obj)) continue;
                var name = NormalizeName(obj["name"] as string);
                var table = obj["table"] as string;
                if (name == null || string.IsNullOrEmpty(table)) continue;
                if (result.Exists(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(new MasterSavedView
                {
                    Name = name,
                    TableName = table,
                    Query = obj["query"] as string ?? string.Empty,
                    ModifiedOnly = obj["modifiedOnly"] is bool modified && modified,
                    SortKey = obj["sortKey"] as string,
                    SortDescending = obj["sortDescending"] is bool descending && descending,
                    ColumnLayout = obj["columns"] as string ?? string.Empty,
                });
            }
            return result;
        }

        static int ReadInt(object value)
        {
            if (value is MasterDataJsonNumber number
                && int.TryParse(number.Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return 0;
        }

        static List<MasterSavedView> Load()
        {
            if (s_views != null) return s_views;
            if (!s_persist)
            {
                s_views = new List<MasterSavedView>();
                return s_views;
            }

            try
            {
                s_views = Deserialize(PlayerPrefs.GetString(PrefsKey, string.Empty));
            }
            catch (Exception)
            {
                s_views = new List<MasterSavedView>();
            }
            return s_views;
        }

        static void Persist()
        {
            if (!s_persist) return;
            try
            {
                PlayerPrefs.SetString(PrefsKey, Serialize(s_views));
                PlayerPrefs.Save();
            }
            catch (Exception)
            {
                // keep the in-memory views
            }
        }

        internal static void ResetForTests()
        {
            s_views = new List<MasterSavedView>();
            s_persist = false;
        }

        internal static void EndTests()
        {
            s_views = null;
            s_persist = true;
        }
    }
}
