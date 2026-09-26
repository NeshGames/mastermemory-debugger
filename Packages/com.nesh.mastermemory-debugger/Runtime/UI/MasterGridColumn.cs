using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>A column of the record grid.</summary>
    internal sealed class MasterGridColumn
    {
        public const float MinWidth = 24f;

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

        public bool Visible { get; set; } = true;

        /// <summary>Frozen columns stay on the left while the others scroll horizontally.</summary>
        public bool Frozen { get; set; }

        public bool DefaultFrozen { get; set; }

        /// <summary>Extra classes of the cells, separated by spaces (number alignment, key color, ...).</summary>
        public string CellClass { get; set; }

        public Action<Label, MasterMemoryRecordDescriptor> Bind { get; }
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
        }

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
                settings[column.Key] = new ColumnSettings { Visible = column.Visible, Frozen = column.Frozen, Width = column.Width };
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
                column.Width = Math.Max(MasterGridColumn.MinWidth, s.Width);
            }
        }

        public static void ResetToDefaults(IReadOnlyList<MasterGridColumn> columns)
        {
            foreach (var column in columns)
            {
                column.Visible = true;
                column.Frozen = column.DefaultFrozen;
                column.Width = column.DefaultWidth;
            }
        }

        internal static void ClearSettings() => s_settings.Clear();
    }
}
