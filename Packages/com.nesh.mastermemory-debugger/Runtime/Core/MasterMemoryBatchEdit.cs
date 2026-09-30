using System;
using System.Collections.Generic;
using System.Globalization;

namespace Nesh.MasterMemoryDebugger
{
    public enum MasterMemoryBatchOperation
    {
        /// <summary>Every record gets the value.</summary>
        Set,
        /// <summary>Numbers: the value is added (negative to subtract).</summary>
        Add,
        /// <summary>Numbers: multiplied by the value; integers are rounded (half away from zero).</summary>
        Multiply,
    }

    public sealed class MasterMemoryBatchEditResult
    {
        /// <summary>Records whose value changed.</summary>
        public int Changed { get; internal set; }

        /// <summary>Records that already had the resulting value.</summary>
        public int Unchanged { get; internal set; }

        /// <summary>Records that could not be changed (null value, overflow, ...), see <see cref="Errors"/>.</summary>
        public int Failed { get; internal set; }

        public List<string> Errors { get; } = new List<string>();

        /// <summary>The value could not be read; nothing was changed.</summary>
        public string InvalidValue { get; internal set; }
    }

    /// <summary>
    /// Changes one field of many records at once (the records found by the search), as overrides:
    /// <c>Damage × 1.1</c>, <c>Cooldown = 2</c>, <c>Price + 100</c>. Keys, lists and complex members can not be batch edited;
    /// members with a converter (<see cref="MasterDataValueKind.Custom"/>) only support Set.
    /// A record whose values all end up equal to the original loses its override.
    /// </summary>
    public static class MasterMemoryBatchEdit
    {
        const int MaxReportedErrors = 20;

        public static bool CanEdit(MasterMemoryFieldDescriptor field)
        {
            if (field == null || field.IsKey || !field.CanEdit || field.IsList) return false;
            switch (field.Kind)
            {
                case MasterDataValueKind.String:
                case MasterDataValueKind.Boolean:
                case MasterDataValueKind.Enum:
                case MasterDataValueKind.FlagsEnum:
                case MasterDataValueKind.Custom:
                    return true;
                default:
                    return IsNumber(field);
            }
        }

        public static bool IsNumber(MasterMemoryFieldDescriptor field) => field != null && (IsInteger(field.Kind) || IsReal(field.Kind));

        /// <summary>Reads the value typed for <paramref name="operation"/>: a value of the field for Set, a number otherwise.</summary>
        public static bool TryParseValue(MasterMemoryFieldDescriptor field, MasterMemoryBatchOperation operation, string text, out object value, out string error)
        {
            value = null;
            error = null;
            text ??= string.Empty;
            if (operation != MasterMemoryBatchOperation.Set)
            {
                if (!IsNumber(field))
                {
                    error = $"{operation} needs a number field.";
                    return false;
                }
                if (!decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    error = $"'{text}' is not a number.";
                    return false;
                }
                value = number;
                return true;
            }

            var trimmed = text.Trim();
            if (field.IsNullable && string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase)) return true;
            try
            {
                switch (field.Kind)
                {
                    case MasterDataValueKind.String:
                        value = text;
                        return true;
                    case MasterDataValueKind.Boolean:
                        if (trimmed == "1" || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)) value = true;
                        else if (trimmed == "0" || string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)) value = false;
                        else throw new FormatException($"'{text}' is not true / false.");
                        return true;
                    case MasterDataValueKind.Enum:
                    case MasterDataValueKind.FlagsEnum:
                        value = MasterDataValueUtility.ParseEnum(field.ValueType, trimmed.Replace('|', ','));
                        return true;
                    case MasterDataValueKind.Custom:
                        return field.Converter.TryParse(trimmed, out value, out error);
                    default:
                        if (IsInteger(field.Kind))
                        {
                            var d = decimal.Parse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture);
                            if (d != decimal.Truncate(d)) throw new FormatException($"'{text}' is not an integer.");
                            value = Convert.ChangeType(d, field.ValueType, CultureInfo.InvariantCulture);
                            return true;
                        }
                        value = Convert.ChangeType(double.Parse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture), field.ValueType, CultureInfo.InvariantCulture);
                        return true;
                }
            }
            catch (Exception e) when (e is FormatException || e is OverflowException || e is ArgumentException || e is InvalidCastException)
            {
                error = e is OverflowException ? $"'{text}' is out of range for {field.ValueType.Name}." : e.Message;
                return false;
            }
        }

        /// <summary>The new value of one record.</summary>
        public static bool TryCompute(MasterMemoryFieldDescriptor field, object current, MasterMemoryBatchOperation operation, object value, out object result, out string error)
        {
            result = null;
            error = null;
            if (operation == MasterMemoryBatchOperation.Set)
            {
                result = value;
                return true;
            }
            if (current == null)
            {
                error = "the value is null";
                return false;
            }

            var operand = (decimal)value;
            try
            {
                if (IsInteger(field.Kind))
                {
                    var number = Convert.ToDecimal(current, CultureInfo.InvariantCulture);
                    number = operation == MasterMemoryBatchOperation.Add ? number + operand : Math.Round(number * operand, MidpointRounding.AwayFromZero);
                    result = Convert.ChangeType(number, field.ValueType, CultureInfo.InvariantCulture);
                    return true;
                }
                var real = Convert.ToDouble(current, CultureInfo.InvariantCulture);
                real = operation == MasterMemoryBatchOperation.Add ? real + (double)operand : real * (double)operand;
                result = Convert.ChangeType(real, field.ValueType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (OverflowException)
            {
                error = $"out of range for {field.ValueType.Name}";
                return false;
            }
        }

        /// <summary>Applies the operation to every record, in one override batch.</summary>
        public static MasterMemoryBatchEditResult Apply(IEnumerable<MasterMemoryRecordDescriptor> records, MasterMemoryFieldDescriptor field, MasterMemoryBatchOperation operation, string valueText)
        {
            var result = new MasterMemoryBatchEditResult();
            if (!MasterMemoryDebugBuild.IsEnabled)
            {
                result.InvalidValue = "Debugger is disabled in this build.";
                return result;
            }
            if (!CanEdit(field))
            {
                result.InvalidValue = $"{field?.Name} can not be batch edited.";
                return result;
            }
            if (!TryParseValue(field, operation, valueText, out var value, out var parseError))
            {
                result.InvalidValue = parseError;
                return result;
            }

            var store = MasterMemoryDebugRuntime.Store;
            using (MasterMemoryDebugRuntime.BeginBatch())
            {
                foreach (var record in records)
                {
                    var current = record.Current;
                    if (current == null || record.IsDeleted)
                    {
                        result.Unchanged++;
                        continue;
                    }
                    var before = field.GetValue(current);
                    if (!TryCompute(field, before, operation, value, out var after, out var error))
                    {
                        AddError(result, $"{record.Table.TableName} {record.KeyText}: {error}");
                        continue;
                    }
                    if (MasterDataValueUtility.AreEqual(before, after))
                    {
                        result.Unchanged++;
                        continue;
                    }

                    var copy = MasterDataCloneUtility.Clone(current);
                    try
                    {
                        field.SetValue(copy, after);
                    }
                    catch (Exception e)
                    {
                        AddError(result, $"{record.Table.TableName} {record.KeyText}: {(e.InnerException ?? e).Message}");
                        continue;
                    }
                    // an added record keeps its override; a changed one equal to the original loses it
                    if (!record.IsAdded && MasterDataDiffUtility.GetChanges(record.Original, copy).Count == 0) store.Remove(record.Table.RecordType, record.PrimaryKey);
                    else store.Set(record.Table.RecordType, record.PrimaryKey, copy);
                    result.Changed++;
                }
            }
            return result;
        }

        /// <summary>Short description for the history and the log: <c>Damage × 1.1</c>.</summary>
        public static string Describe(MasterMemoryFieldDescriptor field, MasterMemoryBatchOperation operation, string valueText)
        {
            var symbol = operation == MasterMemoryBatchOperation.Set ? "=" : operation == MasterMemoryBatchOperation.Add ? "+" : "×";
            return $"{field?.Name} {symbol} {valueText}";
        }

        static void AddError(MasterMemoryBatchEditResult result, string error)
        {
            result.Failed++;
            if (result.Errors.Count < MaxReportedErrors) result.Errors.Add(error);
        }

        static bool IsInteger(MasterDataValueKind kind)
        {
            switch (kind)
            {
                case MasterDataValueKind.Int32:
                case MasterDataValueKind.UInt32:
                case MasterDataValueKind.Int16:
                case MasterDataValueKind.UInt16:
                case MasterDataValueKind.Int64:
                case MasterDataValueKind.UInt64:
                case MasterDataValueKind.Byte:
                case MasterDataValueKind.SByte:
                    return true;
                default:
                    return false;
            }
        }

        static bool IsReal(MasterDataValueKind kind) => kind == MasterDataValueKind.Single || kind == MasterDataValueKind.Double;
    }
}
