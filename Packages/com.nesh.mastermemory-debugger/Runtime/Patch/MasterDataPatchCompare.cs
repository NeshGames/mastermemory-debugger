using System;
using System.Collections.Generic;
using System.Text;

namespace Nesh.MasterMemoryDebugger
{
    public enum MasterDataPatchDifferenceKind
    {
        /// <summary>Both patches change the field, to different values.</summary>
        Different,
        /// <summary>Only the first patch changes the field.</summary>
        OnlyInA,
        /// <summary>Only the second patch changes the field.</summary>
        OnlyInB,
    }

    /// <summary>A field that two patches change differently.</summary>
    public sealed class MasterDataPatchDifference
    {
        public string TableName;
        /// <summary>Primary key as canonical JSON, e.g. <c>{"Id":1001}</c>.</summary>
        public string Key;
        public string Field;
        public MasterDataPatchDifferenceKind Kind;
        /// <summary>JSON value of the first patch (null when it does not change the field).</summary>
        public object ValueA;
        public object ValueB;
        /// <summary>The original value written by either patch, when known.</summary>
        public object Original;
        public bool HasOriginal;
    }

    /// <summary>
    /// Compares two patches field by field, for example a saved patch with the current overrides
    /// (<see cref="MasterDataPatchService.CreatePatch"/>). Fields changed to the same value by both are not listed.
    /// </summary>
    public static class MasterDataPatchCompare
    {
        public static List<MasterDataPatchDifference> Compare(MasterDataPatch a, MasterDataPatch b)
        {
            var valuesA = Index(a);
            var valuesB = Index(b);
            var result = new List<MasterDataPatchDifference>();

            foreach (var pair in valuesA.Values)
            {
                if (valuesB.Keys.TryGetValue(pair.Id, out var other))
                {
                    if (Text(pair.Change.Value) == Text(other.Change.Value)) continue;
                    result.Add(Create(pair, pair.Change.Value, other.Change.Value, MasterDataPatchDifferenceKind.Different, other));
                }
                else
                {
                    result.Add(Create(pair, pair.Change.Value, null, MasterDataPatchDifferenceKind.OnlyInA, null));
                }
            }
            foreach (var pair in valuesB.Values)
            {
                if (valuesA.Keys.ContainsKey(pair.Id)) continue;
                result.Add(Create(pair, null, pair.Change.Value, MasterDataPatchDifferenceKind.OnlyInB, null));
            }

            result.Sort((x, y) =>
            {
                var c = string.CompareOrdinal(x.TableName, y.TableName);
                if (c == 0) c = string.CompareOrdinal(x.Key, y.Key);
                return c != 0 ? c : string.CompareOrdinal(x.Field, y.Field);
            });
            return result;
        }

        /// <summary>Tab separated lines (table, key, field, original, a, b) with a title line, for spreadsheets.</summary>
        public static string ToTsv(IEnumerable<MasterDataPatchDifference> differences, string nameA, string nameB)
        {
            var text = new StringBuilder();
            text.Append("table\tkey\tfield\toriginal\t").Append(Cell(nameA)).Append('\t').Append(Cell(nameB)).Append('\n');
            foreach (var difference in differences)
            {
                text.Append(Cell(difference.TableName)).Append('\t')
                    .Append(Cell(difference.Key)).Append('\t')
                    .Append(Cell(difference.Field)).Append('\t')
                    .Append(difference.HasOriginal ? Cell(Format(difference.Original)) : string.Empty).Append('\t')
                    .Append(difference.Kind == MasterDataPatchDifferenceKind.OnlyInB ? string.Empty : Cell(Format(difference.ValueA))).Append('\t')
                    .Append(difference.Kind == MasterDataPatchDifferenceKind.OnlyInA ? string.Empty : Cell(Format(difference.ValueB))).Append('\n');
            }
            return text.ToString();
        }

        /// <summary>A JSON value as shown in the debugger.</summary>
        public static string Format(object json)
        {
            switch (json)
            {
                case null: return "null";
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case MasterDataJsonNumber n: return n.Raw;
                default: return MasterDataJson.Serialize(json, false);
            }
        }

        sealed class Entry
        {
            public string Id;
            public string TableName;
            public string Key;
            public MasterDataPatchChange Change;
        }

        sealed class Lookup
        {
            public readonly List<Entry> Values = new List<Entry>();
            public readonly Dictionary<string, Entry> Keys = new Dictionary<string, Entry>(StringComparer.Ordinal);
        }

        static Lookup Index(MasterDataPatch patch)
        {
            var lookup = new Lookup();
            if (patch == null) return lookup;
            foreach (var table in patch.Tables)
            {
                foreach (var record in table.Records)
                {
                    var key = record.PrimaryKey.ToCanonicalString();
                    foreach (var change in record.Changes)
                    {
                        var entry = new Entry { TableName = table.TableName, Key = key, Change = change, Id = table.TableName + "\n" + key + "\n" + change.Field };
                        // the last entry of a field wins, like when the patch is applied
                        if (lookup.Keys.TryGetValue(entry.Id, out var previous)) lookup.Values.Remove(previous);
                        lookup.Keys[entry.Id] = entry;
                        lookup.Values.Add(entry);
                    }
                }
            }
            return lookup;
        }

        static MasterDataPatchDifference Create(Entry entry, object a, object b, MasterDataPatchDifferenceKind kind, Entry other)
        {
            var hasOriginal = entry.Change.HasOriginal || (other?.Change.HasOriginal ?? false);
            return new MasterDataPatchDifference
            {
                TableName = entry.TableName,
                Key = entry.Key,
                Field = entry.Change.Field,
                Kind = kind,
                ValueA = a,
                ValueB = b,
                HasOriginal = hasOriginal,
                Original = entry.Change.HasOriginal ? entry.Change.Original : other?.Change.Original,
            };
        }

        static string Text(object json) => json == null ? "null" : MasterDataJson.Serialize(json, false);

        static string Cell(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
        }
    }
}
