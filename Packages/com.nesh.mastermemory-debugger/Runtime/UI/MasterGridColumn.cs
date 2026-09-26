using System;
using System.Collections.Generic;
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
    /// declaration order. Visibility, freezing and widths are remembered per table for the play session.
    /// </summary>
    internal static class MasterGridLayout
    {
        struct ColumnSettings
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

        static readonly Dictionary<string, Dictionary<string, ColumnSettings>> s_settings = new Dictionary<string, Dictionary<string, ColumnSettings>>();

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
            var settings = new Dictionary<string, ColumnSettings>();
            foreach (var column in columns)
            {
                settings[column.Key] = new ColumnSettings { Visible = column.Visible, Frozen = column.Frozen, Width = column.Width, UserSized = column.UserSized };
            }
            s_settings[tableName] = settings;
        }

        /// <summary>Applies the saved settings of the table; columns added since keep their defaults.</summary>
        public static void Restore(string tableName, IReadOnlyList<MasterGridColumn> columns)
        {
            if (string.IsNullOrEmpty(tableName) || !s_settings.TryGetValue(tableName, out var settings)) return;
            foreach (var column in columns)
            {
                if (!settings.TryGetValue(column.Key, out var s)) continue;
                column.Visible = s.Visible;
                column.Frozen = s.Frozen;
                // automatic widths are computed again (labels or data may differ)
                if (s.UserSized)
                {
                    column.UserSized = true;
                    column.Width = Math.Max(MasterGridColumn.MinWidth, s.Width);
                }
            }
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

        internal static void ClearSettings() => s_settings.Clear();
    }
}
