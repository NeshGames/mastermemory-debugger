using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Creates records to add as overrides (New / Duplicate): a record with default values or a copy of another one, with
    /// a new primary key. The record only exists in the override store; a database rebuilt by
    /// <see cref="MasterMemoryDebugRebuild"/> contains it, and <see cref="MasterMemoryDebugRuntime.TryGetOverride{TRecord,TKey}"/>
    /// returns it.
    /// </summary>
    public static class MasterMemoryRecordFactory
    {
        const int MaxObjectDepth = 3;

        /// <summary>
        /// True when records can be added to the table: its primary key members are known (<c>[PrimaryKey]</c>) and writable.
        /// </summary>
        public static bool CanAdd(MasterMemoryTableDescriptor table, out string reason)
        {
            reason = null;
            if (table == null)
            {
                reason = "No table.";
                return false;
            }
            var keys = table.TypeDescriptor.PrimaryKeyFields;
            if (keys.Count == 0)
            {
                reason = $"{table.TableName} has no [PrimaryKey] member, so the key of a new record can not be set.";
                return false;
            }
            foreach (var key in keys)
            {
                if (!key.HasSetter)
                {
                    reason = $"{table.TableName}.{key.Name} (primary key) can not be written.";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// A new record with default values: strings are empty, lists are empty, nested objects are created when they have a
        /// parameterless constructor.
        /// </summary>
        public static object CreateDefault(MasterMemoryTableDescriptor table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            var record = CreateInstance(table.RecordType);
            FillDefaults(record, 0);
            return record;
        }

        /// <summary>Key values for a new record, as text per primary key member: the next free integer, or the source's values.</summary>
        public static List<string> SuggestKey(MasterMemoryTableDescriptor table, object source = null)
        {
            var keys = table.TypeDescriptor.PrimaryKeyFields;
            var result = new List<string>(keys.Count);
            foreach (var key in keys) result.Add(source != null ? MasterDataValueUtility.Format(key.GetValue(source)) : string.Empty);

            // the last integer member: the largest value (among the records sharing the other members) + 1
            var last = keys.Count - 1;
            if (last < 0 || !IsInteger(keys[last].Kind)) return result;
            decimal max = 0;
            var found = false;
            foreach (var record in table.CreateRecordSnapshot())
            {
                var current = record.Current;
                if (current == null) continue;
                var samePrefix = true;
                for (var i = 0; i < last && samePrefix; i++)
                {
                    samePrefix = source == null || MasterDataValueUtility.AreEqual(keys[i].GetValue(current), keys[i].GetValue(source));
                }
                if (!samePrefix) continue;
                var value = Convert.ToDecimal(keys[last].GetValue(current), CultureInfo.InvariantCulture);
                if (!found || value > max) max = value;
                found = true;
            }
            result[last] = (found ? max + 1 : 1).ToString(CultureInfo.InvariantCulture);
            if (source == null)
            {
                for (var i = 0; i < last; i++) result[i] = MasterDataValueUtility.Format(MasterDataValueUtility.CreateDefaultElement(keys[i].ValueType));
            }
            return result;
        }

        /// <summary>
        /// A copy of <paramref name="template"/> (a record of the table, never modified) with the primary key read from
        /// <paramref name="keyTexts"/> (one text per primary key member). Fails when the key is invalid or already used.
        /// </summary>
        public static bool TryCreate(MasterMemoryTableDescriptor table, object template, IReadOnlyList<string> keyTexts, out object record, out object key, out string error)
        {
            record = null;
            key = null;
            if (!CanAdd(table, out error)) return false;
            var keys = table.TypeDescriptor.PrimaryKeyFields;
            if (keyTexts == null || keyTexts.Count != keys.Count)
            {
                error = $"{keys.Count} key values expected.";
                return false;
            }

            var copy = template != null ? MasterDataCloneUtility.Clone(template) : CreateDefault(table);
            for (var i = 0; i < keys.Count; i++)
            {
                if (!MasterMemoryBatchEdit.TryParseValue(keys[i], MasterMemoryBatchOperation.Set, keyTexts[i], out var value, out error))
                {
                    error = $"{keys[i].Name}: {error}";
                    return false;
                }
                if (value == null && keys[i].ValueType.IsValueType && !keys[i].IsNullable)
                {
                    error = $"{keys[i].Name}: a value is required.";
                    return false;
                }
                try
                {
                    keys[i].SetValueUnchecked(copy, value);
                }
                catch (Exception e)
                {
                    error = $"{keys[i].Name}: {(e.InnerException ?? e).Message}";
                    return false;
                }
            }

            try
            {
                key = table.GetPrimaryKey(copy);
            }
            catch (Exception e)
            {
                error = "The primary key can not be read: " + (e.InnerException ?? e).Message;
                return false;
            }
            if (key == null)
            {
                error = "The primary key is null.";
                return false;
            }
            if (table.TryFindOriginal(key, out _))
            {
                error = MasterMemoryDebugRuntime.Store.IsDeleted(table.RecordType, key)
                    ? $"{table.TableName} {MasterDataValueUtility.FormatKey(key)} is a deleted record: Reset it to restore it."
                    : $"{table.TableName} already has a record {MasterDataValueUtility.FormatKey(key)}.";
                return false;
            }
            if (MasterMemoryDebugRuntime.Store.TryGet(table.RecordType, key, out _))
            {
                error = $"{table.TableName} already has an added record {MasterDataValueUtility.FormatKey(key)}.";
                return false;
            }
            record = copy;
            return true;
        }

        /// <summary>A new instance: the parameterless constructor (also a private one), otherwise an uninitialized object.</summary>
        internal static object CreateInstance(Type type)
        {
            try
            {
                return Activator.CreateInstance(type, true);
            }
            catch (MissingMethodException)
            {
                return FormatterServices.GetUninitializedObject(type);
            }
        }

        static void FillDefaults(object instance, int depth)
        {
            foreach (var field in MasterDataReflectionCache.Get(instance.GetType()).Fields)
            {
                if (!field.HasSetter || field.IsNullable) continue;
                object value;
                try
                {
                    value = field.GetValue(instance);
                }
                catch (Exception)
                {
                    continue;
                }
                if (value != null && !(value is Enum)) continue;
                try
                {
                    if (field.Kind == MasterDataValueKind.String) field.SetValueUnchecked(instance, string.Empty);
                    else if (field.Kind == MasterDataValueKind.Enum && value is Enum e && !Enum.IsDefined(field.ValueType, e))
                    {
                        field.SetValueUnchecked(instance, MasterDataValueUtility.CreateDefaultElement(field.ValueType));
                    }
                    else if (field.IsList) field.SetValueUnchecked(instance, MasterDataValueUtility.CreateList(field.FieldType, field.ElementType, Array.Empty<object>()));
                    else if (field.IsObject && value == null && depth < MaxObjectDepth)
                    {
                        var nested = CreateInstance(field.ValueType);
                        FillDefaults(nested, depth + 1);
                        field.SetValueUnchecked(instance, nested);
                    }
                }
                catch (Exception)
                {
                    // leave the default of the type
                }
            }
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
    }
}
