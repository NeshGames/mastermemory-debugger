using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>One member whose value differs between two versions of a record.</summary>
    public readonly struct MasterDataFieldChange
    {
        public MasterDataFieldChange(MasterMemoryFieldDescriptor field, object oldValue, object newValue)
        {
            Field = field;
            OldValue = oldValue;
            NewValue = newValue;
        }

        public MasterMemoryFieldDescriptor Field { get; }
        public string Name => Field.Name;
        public object OldValue { get; }
        public object NewValue { get; }

        public override string ToString() => $"{Name}: {MasterDataValueUtility.Format(OldValue)} → {MasterDataValueUtility.Format(NewValue)}";
    }

    /// <summary>Compares two versions of a record member by member and formats the result.</summary>
    public static class MasterDataDiffUtility
    {
        const string TitleColor = "#FFB84C";
        const string FieldColor = "#FFD27F";
        const string OldValueColor = "#9AA0AC";
        const string NewValueColor = "#7CE38B";

        /// <summary>Members whose values differ. Both records must be of the same type (null records yield no change).</summary>
        public static List<MasterDataFieldChange> GetChanges(object before, object after)
        {
            var changes = new List<MasterDataFieldChange>();
            if (before == null || after == null) return changes;
            if (before.GetType() != after.GetType())
            {
                throw new ArgumentException($"Records of different types can not be compared: {before.GetType().FullName} / {after.GetType().FullName}.");
            }

            foreach (var field in MasterDataReflectionCache.Get(before.GetType()).Fields)
            {
                var oldValue = field.GetValue(before);
                var newValue = field.GetValue(after);
                if (!MasterDataValueUtility.AreEqual(oldValue, newValue)) changes.Add(new MasterDataFieldChange(field, oldValue, newValue));
            }
            return changes;
        }

        /// <summary>
        /// Multi-line text: a title line, then one "Field: old → new" line per change.
        /// With <paramref name="richText"/> the text is colored for the Unity Console.
        /// </summary>
        public static string Format(string title, IReadOnlyList<MasterDataFieldChange> changes, bool richText)
        {
            var sb = new StringBuilder();
            sb.Append(richText ? $"<color={TitleColor}><b>{Escape(title)}</b></color>" : title);
            if (changes.Count == 0)
            {
                sb.Append(richText ? $"\n  <color={OldValueColor}>(no field changed)</color>" : "\n  (no field changed)");
                return sb.ToString();
            }
            foreach (var change in changes) AppendChange(sb, change, richText);
            return sb.ToString();
        }

        /// <summary>True when Console output may contain rich text color tags (the Editor Console renders them, player logs do not).</summary>
        public static bool UseRichText => Application.isEditor;

        internal static void AppendChange(StringBuilder sb, MasterDataFieldChange change, bool richText)
        {
            var oldText = MasterDataValueUtility.Format(change.OldValue);
            var newText = MasterDataValueUtility.Format(change.NewValue);
            if (richText)
            {
                sb.Append($"\n  <color={FieldColor}><b>{Escape(change.Name)}</b></color>: ")
                  .Append($"<color={OldValueColor}>{Escape(oldText)}</color> → <color={NewValueColor}><b>{Escape(newText)}</b></color>");
            }
            else
            {
                sb.Append("\n  ").Append(change.Name).Append(": ").Append(oldText).Append(" → ").Append(newText);
            }
        }

        // Rich text has no escape sequence; a zero width space keeps "<" from starting a tag.
        static string Escape(string text) => text?.Replace("<", "<​");
    }
}
