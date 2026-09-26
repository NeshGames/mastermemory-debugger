using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Display names and tips (tooltips) of tables and fields, per language. The debugger shows the labels of the selected
    /// language (header dropdown) in the table list, the pinned tabs, the grid headers and the inspector; the code names
    /// stay in the tooltips and are still used by search conditions and patches.
    /// <code>
    /// MasterMemoryDebugLocalization.SetTableLabel&lt;SkillMaster&gt;("zh-TW", "技能", "所有技能的基本資料");
    /// MasterMemoryDebugLocalization.SetFieldLabel&lt;SkillMaster&gt;("Damage", "zh-TW", "傷害", "基礎傷害，未含加成");
    /// MasterMemoryDebugLocalization.SetFieldLabel&lt;SkillMaster&gt;("Damage", null, null, "Tip shown in every language");
    /// // or many at once from a spreadsheet (tab separated): table, field, language, label, tip
    /// MasterMemoryDebugLocalization.LoadTsv(textAsset.text);
    /// </code>
    /// A null / empty language sets the tip (or label) used when the selected language has none. Nothing is stored in
    /// release builds.
    /// </summary>
    public static class MasterMemoryDebugLocalization
    {
        /// <summary>Selecting this language shows the code names.</summary>
        public const string CodeNames = "";

        const string PrefsKey = "Nesh.MasterMemoryDebugger.Language";

        struct Text
        {
            public string Label;
            public string Tip;
        }

        // key: "table" or "table.field"; value: language → text
        static readonly Dictionary<string, Dictionary<string, Text>> s_texts = new Dictionary<string, Dictionary<string, Text>>();
        static readonly List<string> s_languages = new List<string>();
        static string s_language;
        static bool s_persist = true;

        /// <summary>Raised when the language or any label changes.</summary>
        public static event Action Changed;

        /// <summary>Languages that have at least one label, in registration order.</summary>
        public static IReadOnlyList<string> Languages => s_languages;

        /// <summary>
        /// The language whose labels are shown; <see cref="CodeNames"/> shows the code names. Remembered in PlayerPrefs.
        /// </summary>
        public static string Language
        {
            get
            {
                if (s_language == null)
                {
                    s_language = CodeNames;
                    if (!s_persist) return s_language;
                    try
                    {
                        s_language = PlayerPrefs.GetString(PrefsKey, CodeNames);
                    }
                    catch (Exception)
                    {
                        s_language = CodeNames;
                    }
                }
                return s_language;
            }
            set
            {
                value ??= CodeNames;
                if (value == Language) return;
                s_language = value;
                if (s_persist)
                {
                    try
                    {
                        PlayerPrefs.SetString(PrefsKey, value);
                        PlayerPrefs.Save();
                    }
                    catch (Exception)
                    {
                        // keep it for this session
                    }
                }
                Changed?.Invoke();
            }
        }

        public static void SetTableLabel<TRecord>(string language, string label, string tip = null)
        {
            SetTableLabel(typeof(TRecord).Name, language, label, tip);
        }

        /// <summary>
        /// Label and tip of a table. <paramref name="tableName"/> is the registered table name (the record class name for
        /// RegisterDatabase) or the <c>[MemoryTable]</c> name.
        /// </summary>
        public static void SetTableLabel(string tableName, string language, string label, string tip = null)
        {
            if (string.IsNullOrEmpty(tableName)) throw new ArgumentException("Table name is required.", nameof(tableName));
            Set(tableName, language, label, tip);
        }

        public static void SetFieldLabel<TRecord>(string fieldName, string language, string label, string tip = null)
        {
            SetFieldLabel(typeof(TRecord).Name, fieldName, language, label, tip);
        }

        public static void SetFieldLabel(string tableName, string fieldName, string language, string label, string tip = null)
        {
            if (string.IsNullOrEmpty(tableName)) throw new ArgumentException("Table name is required.", nameof(tableName));
            if (string.IsNullOrEmpty(fieldName)) throw new ArgumentException("Field name is required.", nameof(fieldName));
            Set(tableName + "." + fieldName, language, label, tip);
        }

        /// <summary>
        /// Loads tab separated lines: <c>table	field	language	label	tip</c> (field empty for the table itself).
        /// A first line starting with "table" is treated as the header; empty lines and lines starting with # are skipped.
        /// Returns the number of entries read.
        /// </summary>
        public static int LoadTsv(string text)
        {
            if (!MasterMemoryDebugBuild.IsEnabled || string.IsNullOrEmpty(text)) return 0;
            var count = 0;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Trim().Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (i == 0 && line.StartsWith("table", StringComparison.OrdinalIgnoreCase)) continue;
                var cells = line.Split('\t');
                if (cells.Length < 4 || cells[0].Trim().Length == 0) continue;
                var table = cells[0].Trim();
                var field = cells[1].Trim();
                var language = cells[2].Trim();
                var label = cells[3].Trim();
                var tip = cells.Length > 4 ? cells[4].Trim().Replace("\\n", "\n") : null;
                // unfilled template line
                if (label.Length == 0 && string.IsNullOrEmpty(tip)) continue;
                Set(field.Length == 0 ? table : table + "." + field, language, label, tip, notify: false);
                count++;
            }
            if (count > 0) Changed?.Invoke();
            return count;
        }

        /// <summary>
        /// Tab separated lines for <see cref="LoadTsv"/>: a line for every registered table and each of its fields, with the
        /// labels and tips already set for <paramref name="language"/> (empty to fill in). With <see cref="CodeNames"/>
        /// the language column is left empty for you to fill in.
        /// </summary>
        public static string CreateTsvTemplate(string language)
        {
            language ??= CodeNames;
            var text = new StringBuilder();
            text.Append("table\tfield\tlanguage\tlabel\ttip\n");
            foreach (var table in MasterMemoryDebugRegistry.Tables)
            {
                AppendTemplateLine(text, table.TableName, string.Empty, language);
                foreach (var field in table.TypeDescriptor.Fields)
                {
                    AppendTemplateLine(text, table.TableName, field.Name, language);
                }
            }
            return text.ToString();
        }

        /// <summary>Every label and tip of every language, in the <see cref="LoadTsv"/> format (remote editor tool).</summary>
        internal static string ExportTsv()
        {
            var text = new StringBuilder();
            text.Append("table\tfield\tlanguage\tlabel\ttip\n");
            foreach (var pair in s_texts)
            {
                // "table" or "table.field"; table names do not contain dots
                var dot = pair.Key.IndexOf('.');
                var table = dot < 0 ? pair.Key : pair.Key.Substring(0, dot);
                var field = dot < 0 ? string.Empty : pair.Key.Substring(dot + 1);
                foreach (var byLanguage in pair.Value)
                {
                    text.Append(table).Append('\t').Append(field).Append('\t').Append(byLanguage.Key).Append('\t')
                        .Append(TemplateCell(byLanguage.Value.Label)).Append('\t')
                        .Append(TemplateCell(byLanguage.Value.Tip)).Append('\n');
                }
            }
            return text.ToString();
        }

        static void AppendTemplateLine(StringBuilder text, string table, string field, string language)
        {
            var key = field.Length == 0 ? table : table + "." + field;
            var entry = default(Text);
            if (s_texts.TryGetValue(key, out var byLanguage)) byLanguage.TryGetValue(language, out entry);
            text.Append(table).Append('\t')
                .Append(field).Append('\t')
                .Append(language).Append('\t')
                .Append(TemplateCell(entry.Label)).Append('\t')
                .Append(TemplateCell(entry.Tip)).Append('\n');
        }

        // LoadTsv reads \n back as a line break
        static string TemplateCell(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\r", string.Empty).Replace("\n", "\\n").Replace('\t', ' ');
        }

        public static void Clear()
        {
            s_texts.Clear();
            s_languages.Clear();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ lookup

        public static string GetTableLabel(MasterMemoryTableDescriptor table)
        {
            if (table == null) return string.Empty;
            return Find(table, null).Label ?? table.TableName;
        }

        /// <summary>Code name, then the tip of the selected language (or the language independent tip).</summary>
        public static string GetTableTooltip(MasterMemoryTableDescriptor table)
        {
            if (table == null) return string.Empty;
            return Compose(table.TableName, Find(table, null).Tip);
        }

        public static string GetFieldLabel(MasterMemoryTableDescriptor table, MasterMemoryFieldDescriptor field)
        {
            if (field == null) return string.Empty;
            return Find(table, field.Name).Label ?? field.Name;
        }

        public static string GetFieldTooltip(MasterMemoryTableDescriptor table, MasterMemoryFieldDescriptor field)
        {
            if (field == null) return string.Empty;
            return Compose($"{field.Name} ({field.FieldType.Name})", Find(table, field.Name).Tip);
        }

        /// <summary>Label of a field by record type (search completion), or null when there is none.</summary>
        internal static string FindFieldLabel(MasterMemoryTableDescriptor table, string fieldName)
        {
            return Find(table, fieldName).Label;
        }

        internal static void ResetForTests()
        {
            s_texts.Clear();
            s_languages.Clear();
            s_language = CodeNames;
            // tests never write the language of the developer's debugger
            s_persist = false;
        }

        internal static void EndTests()
        {
            s_texts.Clear();
            s_languages.Clear();
            s_language = null;
            s_persist = true;
        }

        static void Set(string key, string language, string label, string tip, bool notify = true)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return;
            language ??= string.Empty;
            if (!s_texts.TryGetValue(key, out var byLanguage)) s_texts.Add(key, byLanguage = new Dictionary<string, Text>());
            byLanguage.TryGetValue(language, out var text);
            if (!string.IsNullOrEmpty(label)) text.Label = label;
            if (!string.IsNullOrEmpty(tip)) text.Tip = tip;
            byLanguage[language] = text;
            if (language.Length > 0 && !string.IsNullOrEmpty(label) && !s_languages.Contains(language)) s_languages.Add(language);
            if (notify) Changed?.Invoke();
        }

        /// <summary>
        /// Text of the selected language, falling back to the language independent one (per label / tip).
        /// Tables are looked up by registered name, then by [MemoryTable] name.
        /// </summary>
        static Text Find(MasterMemoryTableDescriptor table, string field)
        {
            var result = default(Text);
            if (table == null) return result;
            if (!TryGet(table.TableName, field, out var byLanguage) && !TryGet(table.MemoryTableName, field, out byLanguage)) return result;

            var language = Language;
            if (language.Length > 0 && byLanguage.TryGetValue(language, out var selected)) result = selected;
            if (byLanguage.TryGetValue(string.Empty, out var common))
            {
                // language independent labels are shown only when a language is selected
                if (result.Label == null && language.Length > 0) result.Label = common.Label;
                result.Tip ??= common.Tip;
            }
            return result;
        }

        static bool TryGet(string tableName, string field, out Dictionary<string, Text> byLanguage)
        {
            byLanguage = null;
            if (string.IsNullOrEmpty(tableName)) return false;
            return s_texts.TryGetValue(field == null ? tableName : tableName + "." + field, out byLanguage);
        }

        static string Compose(string codeName, string tip)
        {
            if (string.IsNullOrEmpty(tip)) return codeName;
            return new StringBuilder(codeName).Append('\n').Append(tip).ToString();
        }
    }
}
