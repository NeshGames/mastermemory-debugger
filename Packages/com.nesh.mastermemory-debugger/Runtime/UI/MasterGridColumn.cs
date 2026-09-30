using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>A column of the record grid.</summary>
    internal sealed class MasterGridColumn
    {
        public const float MinWidth = 24f;

        bool visible = true;
        bool frozen;

        public MasterGridColumn(string key, string title, float width, Action<Label, MasterMemoryRecordDescriptor> bind)
        {
            Key = key;
            Title = title;
            DefaultWidth = width;
            Width = width;
            Bind = bind;
        }

        /// <summary>Member name, or a reserved key for the state / display columns. Used for sorting and saved settings.</summary>
        public string Key { get; }

        public string Title { get; }

        public string Tooltip { get; set; }

        public float DefaultWidth { get; }

        public float Width { get; set; }

        /// <summary>The user dragged the width; automatic sizing leaves the column alone.</summary>
        public bool UserSized { get; set; }

        /// <summary>False for columns with a fixed width (the state column).</summary>
        public bool AutoWidth { get; set; } = true;

        /// <summary>Always visible and frozen (state and primary key columns); not listed as switchable.</summary>
        public bool Locked { get; set; }

        public bool Visible
        {
            get => Locked || visible;
            set => visible = value;
        }

        /// <summary>Frozen columns stay on the left while the others scroll horizontally.</summary>
        public bool Frozen
        {
            get => Locked || frozen;
            set => frozen = value;
        }

        public bool DefaultFrozen { get; set; }

        /// <summary>Extra classes of the cells, separated by spaces (number alignment, key color, ...).</summary>
        public string CellClass { get; set; }

        public Action<Label, MasterMemoryRecordDescriptor> Bind { get; }

        /// <summary>Text shown in a cell, used to size the column automatically.</summary>
        public Func<MasterMemoryRecordDescriptor, string> Text { get; set; }
    }

    /// <summary>
    /// Column order and settings of the grid: visible frozen columns first, then the visible scrolled ones, each group in
    /// declaration order. Visibility, freezing and dragged widths are remembered per table in PlayerPrefs (per device).
    /// </summary>
    internal static class MasterGridLayout
    {
        internal struct ColumnSettings
        {
            public bool Visible;
            public bool Frozen;
            public float Width;
            public bool UserSized;
        }

        /// <summary>Automatic widths stay within these bounds (a dragged width does not).</summary>
        public const float MinAutoWidth = 40f;
        public const float MaxAutoWidth = 320f;

        const float HeaderFontSize = 12f;
        const float CellFontSize = 12f;
        // padding + border of a cell, and room for the sort arrow in the header
        const float CellPadding = 14f;
        const float HeaderExtra = 28f;
        const int MaxSampledRows = 200;
        const string PrefsKeyPrefix = "Nesh.MasterMemoryDebugger.Columns.";

        static readonly Dictionary<string, Dictionary<string, ColumnSettings>> s_settings = new Dictionary<string, Dictionary<string, ColumnSettings>>();
        static bool s_persist = true;

        public static void Split(IReadOnlyList<MasterGridColumn> columns, List<MasterGridColumn> frozen, List<MasterGridColumn> scrolled)
        {
            frozen.Clear();
            scrolled.Clear();
            foreach (var column in columns)
            {
                if (!column.Visible) continue;
                (column.Frozen ? frozen : scrolled).Add(column);
            }
        }

        public static float TotalWidth(IReadOnlyList<MasterGridColumn> columns)
        {
            var width = 0f;
            foreach (var column in columns) width += column.Width;
            return width;
        }

        /// <summary>Largest horizontal scroll offset of the scrolled part.</summary>
        public static float MaxScroll(float contentWidth, float viewportWidth) => Math.Max(0f, contentWidth - Math.Max(0f, viewportWidth));

        public static void Save(string tableName, IReadOnlyList<MasterGridColumn> columns)
        {
            if (string.IsNullOrEmpty(tableName)) return;
            var settings = CaptureSettings(columns);
            s_settings[tableName] = settings;
            if (s_persist) Write(tableName, settings);
        }

        /// <summary>Serializable snapshot used by Saved Views; independent of the table's default PlayerPrefs layout.</summary>
        public static string Capture(IReadOnlyList<MasterGridColumn> columns) =>
            Serialize(CaptureSettings(columns));

        /// <summary>
        /// Applies a serialized Saved View layout. Unknown old columns are ignored and newly added columns keep defaults.
        /// </summary>
        public static void ApplySerialized(string text, IReadOnlyList<MasterGridColumn> columns)
        {
            ResetToDefaults(columns);
            ApplySettings(Deserialize(text), columns);
        }

        static Dictionary<string, ColumnSettings> CaptureSettings(IReadOnlyList<MasterGridColumn> columns)
        {
            var settings = new Dictionary<string, ColumnSettings>();
            if (columns == null) return settings;
            foreach (var column in columns)
            {
                settings[column.Key] = new ColumnSettings
                {
                    Visible = column.Visible,
                    Frozen = column.Frozen,
                    Width = column.Width,
                    UserSized = column.UserSized,
                };
            }
            return settings;
        }

        static void ApplySettings(Dictionary<string, ColumnSettings> settings, IReadOnlyList<MasterGridColumn> columns)
        {
            if (settings == null || columns == null) return;
            foreach (var column in columns)
            {
                if (!settings.TryGetValue(column.Key, out var s)) continue;
                column.Visible = s.Visible;
                column.Frozen = s.Frozen;
                if (s.UserSized)
                {
                    column.UserSized = true;
                    column.Width = Math.Max(MasterGridColumn.MinWidth, s.Width);
                }
            }
        }

        /// <summary>Applies the saved settings of the table; columns added since keep their defaults.</summary>
        public static void Restore(string tableName, IReadOnlyList<MasterGridColumn> columns)
        {
            if (string.IsNullOrEmpty(tableName)) return;
            if (!s_settings.TryGetValue(tableName, out var settings))
            {
                settings = s_persist ? Read(tableName) : null;
                if (settings == null) return;
                s_settings[tableName] = settings;
            }
            ApplySettings(settings, columns);
        }

        public static void ResetToDefaults(IReadOnlyList<MasterGridColumn> columns)
        {
            foreach (var column in columns)
            {
                column.Visible = true;
                column.Frozen = column.DefaultFrozen;
                column.Width = column.DefaultWidth;
                column.UserSized = false;
            }
        }

        /// <summary>
        /// Sizes every column that the user did not resize to fit its title and the values of up to 200 rows,
        /// between <see cref="MinAutoWidth"/> and <see cref="MaxAutoWidth"/>.
        /// </summary>
        public static void AutoFit(IReadOnlyList<MasterGridColumn> columns, IReadOnlyList<MasterMemoryRecordDescriptor> rows)
        {
            foreach (var column in columns)
            {
                if (!column.UserSized) AutoFit(column, rows);
            }
        }

        public static void AutoFit(MasterGridColumn column, IReadOnlyList<MasterMemoryRecordDescriptor> rows)
        {
            if (!column.AutoWidth) return;
            var width = EstimateTextWidth(column.Title, HeaderFontSize) * 1.08f + HeaderExtra;
            if (column.Text != null && rows != null)
            {
                var count = Math.Min(rows.Count, MaxSampledRows);
                for (var i = 0; i < count; i++)
                {
                    string text;
                    try
                    {
                        text = column.Text(rows[i]);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    width = Math.Max(width, EstimateTextWidth(text, CellFontSize) + CellPadding);
                }
            }
            column.Width = Math.Min(MaxAutoWidth, Math.Max(MinAutoWidth, (float)Math.Ceiling(width)));
        }

        /// <summary>
        /// Approximate width of a single line of text: full width characters (CJK, kana, hangul, full width forms) count
        /// as one em, wide Latin letters as 0.72 em, the rest as 0.56 em. Good enough for column sizes without a layout pass.
        /// </summary>
        public static float EstimateTextWidth(string text, float fontSize)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            var em = 0f;
            foreach (var c in text)
            {
                if (c == '\n') break;
                if (IsFullWidth(c)) em += 1f;
                else if (c == 'M' || c == 'W' || c == 'm' || c == 'w' || c == '@') em += 0.8f;
                else if (char.IsUpper(c)) em += 0.66f;
                else if (c == 'i' || c == 'l' || c == 'j' || c == 'I' || c == '.' || c == ',' || c == ':' || c == '|' || c == '\'' || c == ' ') em += 0.3f;
                else em += 0.56f;
            }
            return em * fontSize;
        }

        static bool IsFullWidth(char c)
        {
            return (c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0xA4CF) || (c >= 0xAC00 && c <= 0xD7A3)
                   || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE30 && c <= 0xFE4F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6);
        }

        /// <summary>One line per column: key, visible, frozen, width, user sized (tab separated).</summary>
        internal static string Serialize(Dictionary<string, ColumnSettings> settings)
        {
            var text = new StringBuilder();
            foreach (var pair in settings)
            {
                var s = pair.Value;
                text.Append(pair.Key).Append('\t')
                    .Append(s.Visible ? '1' : '0').Append('\t')
                    .Append(s.Frozen ? '1' : '0').Append('\t')
                    .Append(s.Width.ToString("0.#", CultureInfo.InvariantCulture)).Append('\t')
                    .Append(s.UserSized ? '1' : '0').Append('\n');
            }
            return text.ToString();
        }

        internal static Dictionary<string, ColumnSettings> Deserialize(string text)
        {
            var settings = new Dictionary<string, ColumnSettings>();
            if (string.IsNullOrEmpty(text)) return settings;
            foreach (var line in text.Split('\n'))
            {
                var cells = line.Split('\t');
                if (cells.Length < 5 || cells[0].Length == 0) continue;
                if (!float.TryParse(cells[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)) continue;
                settings[cells[0]] = new ColumnSettings { Visible = cells[1] == "1", Frozen = cells[2] == "1", Width = width, UserSized = cells[4] == "1" };
            }
            return settings;
        }

        static Dictionary<string, ColumnSettings> Read(string tableName)
        {
            try
            {
                var text = PlayerPrefs.GetString(PrefsKeyPrefix + tableName, string.Empty);
                return text.Length == 0 ? null : Deserialize(text);
            }
            catch (Exception)
            {
                // PlayerPrefs unavailable: defaults
                return null;
            }
        }

        static void Write(string tableName, Dictionary<string, ColumnSettings> settings)
        {
            try
            {
                PlayerPrefs.SetString(PrefsKeyPrefix + tableName, Serialize(settings));
                PlayerPrefs.Save();
            }
            catch (Exception)
            {
                // kept for this session only
            }
        }

        /// <summary>Tests: forget the settings and never read or write PlayerPrefs.</summary>
        internal static void ResetForTests()
        {
            s_settings.Clear();
            s_persist = false;
        }

        internal static void EndTests()
        {
            s_settings.Clear();
            s_persist = true;
        }
    }
}
