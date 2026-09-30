using System;
using System.Collections.Generic;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>One value of an import: a field of a record and its new value.</summary>
    public sealed class MasterMemoryTsvImportValue
    {
        internal MasterMemoryTsvImportValue(MasterMemoryRecordDescriptor record, MasterMemoryFieldDescriptor field, object value)
        {
            Record = record;
            Field = field;
            Value = value;
        }

        public MasterMemoryRecordDescriptor Record { get; }
        public MasterMemoryFieldDescriptor Field { get; }
        public object Value { get; }
    }

    /// <summary>What an import would change, read by <see cref="MasterMemoryTsvImport.Read"/>.</summary>
    public sealed class MasterMemoryTsvImportPlan
    {
        const int MaxReportedProblems = 50;

        /// <summary>Values that differ from the current ones.</summary>
        public List<MasterMemoryTsvImportValue> Changes { get; } = new List<MasterMemoryTsvImportValue>();

        /// <summary>Lines whose value equals the current one.</summary>
        public int Unchanged { get; internal set; }

        /// <summary>Lines that can not be imported (unknown table / record / field, invalid value); nothing is done for them.</summary>
        public int Failed { get; internal set; }

        /// <summary>Lines whose <c>original</c> column no longer matches the original record (the master data changed since the export).</summary>
        public int Outdated { get; internal set; }

        /// <summary>The first problems, with their line numbers.</summary>
        public List<string> Problems { get; } = new List<string>();

        /// <summary>The text has no <c>table</c> / <c>key</c> / <c>field</c> / <c>current</c> title line; nothing can be imported.</summary>
        public string InvalidFormat { get; internal set; }

        public int RecordCount
        {
            get
            {
                var records = new HashSet<MasterMemoryRecordDescriptor>();
                foreach (var change in Changes) records.Add(change.Record);
                return records.Count;
            }
        }

        internal void AddProblem(int line, string message)
        {
            if (Problems.Count < MaxReportedProblems) Problems.Add($"line {line}: {message}");
        }
    }

    /// <summary>
    /// Imports values edited in a spreadsheet as overrides. The text is tab separated with a title line naming the columns
    /// <c>table</c>, <c>key</c>, <c>field</c> and <c>current</c> (or <c>value</c>), in any order; other columns are ignored.
    /// This is the format of the Changes tab's Copy TSV (<see cref="MasterMemoryChangeSummary.ToTsv"/>): copy, edit the
    /// current column, paste back. With an <c>original</c> column, lines whose original value changed since are counted as
    /// <see cref="MasterMemoryTsvImportPlan.Outdated"/> (still imported).
    /// <code>
    /// var plan = MasterMemoryTsvImport.Read(text);   // preview: plan.Changes, plan.Problems
    /// MasterMemoryTsvImport.Apply(plan);
    /// </code>
    /// Keys are matched as shown in the debugger (<c>1001</c>, <c>(2, 1)</c> for composite keys). Keys, lists and complex
    /// members can not be imported; members with a converter are read with it (<see cref="MasterMemoryBatchEdit.CanEdit"/>).
    /// </summary>
    public static class MasterMemoryTsvImport
    {
        public static MasterMemoryTsvImportPlan Read(string text)
        {
            var plan = new MasterMemoryTsvImportPlan();
            var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            var header = -1;
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length > 0)
                {
                    header = i;
                    break;
                }
            }
            var columns = header < 0 ? null : ReadColumns(lines[header]);
            if (columns == null)
            {
                plan.InvalidFormat = "The first line must name the columns table, key, field and current (tab separated), like the Changes tab's Copy TSV.";
                return plan;
            }

            var records = new Dictionary<MasterMemoryTableDescriptor, Dictionary<string, MasterMemoryRecordDescriptor>>();
            // a field given twice: the last line wins
            var values = new Dictionary<(MasterMemoryRecordDescriptor record, MasterMemoryFieldDescriptor field), (string text, object value)>();
            var order = new List<(MasterMemoryRecordDescriptor record, MasterMemoryFieldDescriptor field)>();

            for (var i = header + 1; i < lines.Length; i++)
            {
                var lineNumber = i + 1;
                if (lines[i].Trim().Length == 0) continue;
                var cells = lines[i].Split('\t');
                string Cell(int index) => index >= 0 && index < cells.Length ? cells[index].Trim() : null;

                var tableName = Cell(columns.Table);
                var keyText = Cell(columns.Key);
                var fieldName = Cell(columns.Field);
                var valueText = columns.Value < cells.Length ? cells[columns.Value].Trim() : null;
                if (string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(keyText) || string.IsNullOrEmpty(fieldName) || valueText == null)
                {
                    Fail(plan, lineNumber, "table, key, field or value is missing");
                    continue;
                }
                // a deleted record in Copy TSV: nothing to import
                if (fieldName == MasterMemoryChangeSummary.DeletedField) continue;
                if (!MasterMemoryDebugRegistry.TryGetTable(tableName, out var table))
                {
                    Fail(plan, lineNumber, $"table {tableName} is not registered");
                    continue;
                }
                if (!table.TypeDescriptor.TryGetField(fieldName, out var field) || !MasterMemoryBatchEdit.CanEdit(field))
                {
                    Fail(plan, lineNumber, $"{tableName}.{fieldName} is not a field that can be imported");
                    continue;
                }
                if (!records.TryGetValue(table, out var byKey)) records.Add(table, byKey = IndexByKey(table));
                if (!byKey.TryGetValue(keyText, out var record))
                {
                    Fail(plan, lineNumber, $"{tableName} has no record {keyText}");
                    continue;
                }
                if (record.IsDeleted || record.Current == null)
                {
                    Fail(plan, lineNumber, $"{tableName} {keyText} is deleted");
                    continue;
                }
                if (!MasterMemoryBatchEdit.TryParseValue(field, MasterMemoryBatchOperation.Set, valueText, out var value, out var error))
                {
                    Fail(plan, lineNumber, $"{tableName} {keyText} {fieldName}: {error}");
                    continue;
                }

                var originalText = Cell(columns.Original);
                var original = record.IsAdded ? string.Empty : CellText(field.GetValue(record.Original));
                if (originalText != null && !record.IsAdded && originalText != original)
                {
                    plan.Outdated++;
                    plan.AddProblem(lineNumber, $"{tableName} {keyText} {fieldName}: the original value is now {original}, not {originalText} (imported anyway)");
                }

                if (!values.ContainsKey((record, field))) order.Add((record, field));
                values[(record, field)] = (valueText, value);
            }

            foreach (var key in order)
            {
                var (valueText, value) = values[key];
                var current = key.field.GetValue(key.record.Current);
                // the text of the current value (a line left as exported, even if Copy TSV flattened line breaks), or an equal value
                if (valueText == CellText(current) || MasterDataValueUtility.AreEqual(current, value))
                {
                    plan.Unchanged++;
                    continue;
                }
                plan.Changes.Add(new MasterMemoryTsvImportValue(key.record, key.field, value));
            }
            return plan;
        }

        /// <summary>Applies the changes of <paramref name="plan"/> as overrides, one per record, in one override batch.</summary>
        public static int Apply(MasterMemoryTsvImportPlan plan)
        {
            if (plan == null || !MasterMemoryDebugBuild.IsEnabled || plan.Changes.Count == 0) return 0;

            var byRecord = new Dictionary<MasterMemoryRecordDescriptor, List<MasterMemoryTsvImportValue>>();
            var order = new List<MasterMemoryRecordDescriptor>();
            foreach (var change in plan.Changes)
            {
                if (!byRecord.TryGetValue(change.Record, out var list))
                {
                    byRecord.Add(change.Record, list = new List<MasterMemoryTsvImportValue>());
                    order.Add(change.Record);
                }
                list.Add(change);
            }

            var store = MasterMemoryDebugRuntime.Store;
            var applied = 0;
            using (MasterMemoryDebugRuntime.BeginBatch())
            {
                foreach (var record in order)
                {
                    var copy = MasterDataCloneUtility.Clone(record.Current);
                    foreach (var change in byRecord[record]) change.Field.SetValue(copy, change.Value);
                    // an added record keeps its override; a changed one equal to the original loses it
                    if (!record.IsAdded && MasterDataDiffUtility.GetChanges(record.Original, copy).Count == 0) store.Remove(record.Table.RecordType, record.PrimaryKey);
                    else store.Set(record.Table.RecordType, record.PrimaryKey, copy);
                    applied += byRecord[record].Count;
                }
            }
            return applied;
        }

        sealed class Columns
        {
            public int Table = -1;
            public int Key = -1;
            public int Field = -1;
            public int Value = -1;
            public int Original = -1;
        }

        static Columns ReadColumns(string line)
        {
            var columns = new Columns();
            var cells = line.Split('\t');
            for (var i = 0; i < cells.Length; i++)
            {
                switch (cells[i].Trim().ToLowerInvariant())
                {
                    case "table": columns.Table = i; break;
                    case "key": columns.Key = i; break;
                    case "field": columns.Field = i; break;
                    case "current":
                    case "value":
                        columns.Value = i;
                        break;
                    case "original": columns.Original = i; break;
                }
            }
            return columns.Table < 0 || columns.Key < 0 || columns.Field < 0 || columns.Value < 0 ? null : columns;
        }

        static Dictionary<string, MasterMemoryRecordDescriptor> IndexByKey(MasterMemoryTableDescriptor table)
        {
            var result = new Dictionary<string, MasterMemoryRecordDescriptor>();
            foreach (var record in table.CreateRecordSnapshot()) result[record.KeyText] = record;
            return result;
        }

        /// <summary>A value as Copy TSV writes it.</summary>
        static string CellText(object value)
        {
            return MasterDataValueUtility.Format(value).Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
        }

        static void Fail(MasterMemoryTsvImportPlan plan, int line, string message)
        {
            plan.Failed++;
            plan.AddProblem(line, message);
        }
    }
}
