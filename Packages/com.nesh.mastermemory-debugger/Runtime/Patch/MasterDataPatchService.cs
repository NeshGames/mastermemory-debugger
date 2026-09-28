using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Nesh.MasterMemoryDebugger
{
    public enum MasterDataPatchApplyStatus
    {
        Applied,
        /// <summary>The patch was created for a different master version. Nothing was applied; retry with force.</summary>
        VersionMismatch,
        /// <summary>The patch was created for a different registered schema. Nothing was applied; retry with force.</summary>
        SchemaMismatch,
        /// <summary>The patch format is not supported. Nothing was applied.</summary>
        UnsupportedFormat,
        /// <summary>Patch preflight failed. Nothing was applied.</summary>
        Invalid,
        /// <summary>The override layer changed after preflight. Nothing was applied.</summary>
        Conflict,
        /// <summary>The debugger is disabled in this build. Nothing was applied.</summary>
        Disabled,
    }

    public sealed class MasterDataPatchApplyResult
    {
        public MasterDataPatchApplyStatus Status;
        public string PatchMasterVersion;
        public string CurrentMasterVersion;
        public string PatchSchemaHash;
        public string CurrentSchemaHash;
        public int AppliedRecords;
        public int AppliedFields;
        /// <summary>Records added (included in <see cref="AppliedRecords"/>).</summary>
        public int AddedRecords;
        /// <summary>Records deleted (included in <see cref="AppliedRecords"/>).</summary>
        public int DeletedRecords;
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public bool Succeeded => Status == MasterDataPatchApplyStatus.Applied;
    }

    /// <summary>Creates patches from the current overrides and applies patches to the override store.</summary>
    public static class MasterDataPatchService
    {
        // ------------------------------------------------------------------ create

        /// <summary>
        /// Builds a patch of every override whose record type is registered.
        /// Only editable fields whose value differs from the original are written; added records are written with all their
        /// values, deleted ones with their key.
        /// </summary>
        public static MasterDataPatch CreatePatch(List<string> warnings = null)
        {
            var patch = new MasterDataPatch
            {
                MasterVersion = MasterMemoryDebugRegistry.GetMasterVersion(),
                SchemaHash = MasterMemoryDebugRegistry.GetSchemaHash(),
                ExportedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            };
            if (!MasterMemoryDebugBuild.IsEnabled) return patch;

            var entriesByType = new Dictionary<Type, List<MasterDataOverrideEntry>>();
            foreach (var entry in MasterMemoryDebugRuntime.Store.GetEntries())
            {
                if (!entriesByType.TryGetValue(entry.Key.RecordType, out var list))
                {
                    entriesByType.Add(entry.Key.RecordType, list = new List<MasterDataOverrideEntry>());
                }
                list.Add(entry);
            }

            foreach (var table in MasterMemoryDebugRegistry.Tables)
            {
                if (!entriesByType.TryGetValue(table.RecordType, out var entries)) continue;
                entriesByType.Remove(table.RecordType);

                var originals = BuildOriginalLookupByKey(table);
                var patchTable = new MasterDataPatchTable
                {
                    TableName = table.TableName,
                    MemoryTableName = table.MemoryTableName,
                    RecordType = table.RecordType.FullName,
                };

                foreach (var entry in entries)
                {
                    var hasOriginal = originals.TryGetValue(entry.Key.PrimaryKey, out var original);
                    if (entry.IsDeleted)
                    {
                        if (hasOriginal) patchTable.Records.Add(new MasterDataPatchRecord { PrimaryKey = CreatePrimaryKeyJson(table, original), Deleted = true });
                        else warnings?.Add($"{table.TableName} {MasterDataValueUtility.FormatKey(entry.Key.PrimaryKey)}: deleted record not found in the master data, skipped.");
                        continue;
                    }
                    if (!hasOriginal)
                    {
                        patchTable.Records.Add(CreateAddedRecord(table, entry.Value, warnings));
                        continue;
                    }
                    var record = CreatePatchRecord(table, original, entry.Value, warnings);
                    if (record.Changes.Count > 0) patchTable.Records.Add(record);
                }

                if (patchTable.Records.Count > 0) patch.Tables.Add(patchTable);
            }

            foreach (var type in entriesByType.Keys)
            {
                warnings?.Add($"Overrides of {type.FullName} were skipped because the table is not registered.");
            }
            patch.FormatVersion = MasterDataPatch.BasicFormatVersion;
            foreach (var table in patch.Tables)
            {
                foreach (var record in table.Records)
                {
                    if (record.Added || record.Deleted) patch.FormatVersion = MasterDataPatch.CurrentFormatVersion;
                }
            }
            return patch;
        }

        /// <summary>A record that only exists as an override: every writable non primary key member, without originals.</summary>
        public static MasterDataPatchRecord CreateAddedRecord(MasterMemoryTableDescriptor table, object record, List<string> warnings = null)
        {
            var result = new MasterDataPatchRecord { PrimaryKey = CreatePrimaryKeyJson(table, record), Added = true };
            foreach (var field in table.TypeDescriptor.Fields)
            {
                if (field.IsPrimaryKey || !field.HasSetter) continue;
                object json;
                try
                {
                    json = MasterDataValueUtility.ToJson(field.GetValue(record));
                }
                catch (NotSupportedException)
                {
                    warnings?.Add($"{table.TableName} {result.PrimaryKey.ToCanonicalString()} (added): field '{field.Name}' can not be exported; it gets its default value when the patch is applied.");
                    continue;
                }
                result.Changes.Add(new MasterDataPatchChange { Field = field.Name, Value = json, HasOriginal = false });
            }
            return result;
        }

        public static string CreatePatchJson(List<string> warnings = null)
        {
            return MasterDataPatchSerializer.ToJson(CreatePatch(warnings));
        }

        /// <summary>Changed fields of one record (editable, non-key fields: simple values, lists of them and nested objects).</summary>
        public static MasterDataPatchRecord CreatePatchRecord(MasterMemoryTableDescriptor table, object original, object current, List<string> warnings = null)
        {
            var record = new MasterDataPatchRecord { PrimaryKey = CreatePrimaryKeyJson(table, original) };
            foreach (var field in table.TypeDescriptor.Fields)
            {
                if (field.IsKey) continue;
                var originalValue = field.GetValue(original);
                var currentValue = field.GetValue(current);
                if (MasterDataValueUtility.AreEqual(originalValue, currentValue)) continue;

                if (!field.CanEdit)
                {
                    warnings?.Add($"{table.TableName} {record.PrimaryKey.ToCanonicalString()}: field '{field.Name}' differs but can not be exported.");
                    continue;
                }
                object originalJson, valueJson;
                try
                {
                    originalJson = MasterDataValueUtility.ToJson(originalValue);
                    valueJson = MasterDataValueUtility.ToJson(currentValue);
                }
                catch (NotSupportedException e)
                {
                    warnings?.Add($"{table.TableName} {record.PrimaryKey.ToCanonicalString()}: field '{field.Name}' can not be exported ({e.Message}).");
                    continue;
                }
                if (field.IsObject && MasterDataJson.Serialize(originalJson, false) == MasterDataJson.Serialize(valueJson, false))
                {
                    // only read-only members of the nested object differ (changed by code)
                    warnings?.Add($"{table.TableName} {record.PrimaryKey.ToCanonicalString()}: field '{field.Name}' differs in members that can not be exported.");
                    continue;
                }
                record.Changes.Add(new MasterDataPatchChange
                {
                    Field = field.Name,
                    Original = originalJson,
                    Value = valueJson,
                });
            }
            return record;
        }

        /// <summary>
        /// Primary key as JSON: <c>{"Id": 1001}</c>, <c>{"GroupId": 1, "Level": 2}</c>.
        /// When no [PrimaryKey] member is known the key object is written as <c>{"key": ...}</c> / <c>{"Item1": ..., "Item2": ...}</c>.
        /// </summary>
        public static MasterDataJsonObject CreatePrimaryKeyJson(MasterMemoryTableDescriptor table, object record)
        {
            var json = new MasterDataJsonObject();
            var keyFields = table.TypeDescriptor.PrimaryKeyFields;
            if (keyFields.Count > 0)
            {
                foreach (var field in keyFields)
                {
                    json.Add(field.Name, MasterDataValueUtility.ToJson(field.GetValue(record)));
                }
                return json;
            }

            var key = table.GetPrimaryKey(record);
            if (key is ITuple tuple)
            {
                for (var i = 0; i < tuple.Length; i++) json.Add("Item" + (i + 1), MasterDataValueUtility.ToJson(tuple[i]));
            }
            else
            {
                json.Add("key", MasterDataValueUtility.ToJson(key));
            }
            return json;
        }

        // ------------------------------------------------------------------ apply

        /// <summary>
        /// Applies a patch.
        /// </summary>
        /// <param name="force">Apply even when the master version differs.</param>
        /// <param name="replaceExisting">Clear all current overrides first.</param>
        public static MasterDataPatchApplyResult Apply(MasterDataPatch patch, bool force = false, bool replaceExisting = true)
        {
            if (patch == null) throw new ArgumentNullException(nameof(patch));
            var result = new MasterDataPatchApplyResult
            {
                PatchMasterVersion = patch.MasterVersion ?? MasterMemoryDebugRegistry.UnknownMasterVersion,
                CurrentMasterVersion = MasterMemoryDebugRegistry.GetMasterVersion(),
                PatchSchemaHash = patch.SchemaHash,
                CurrentSchemaHash = MasterMemoryDebugRegistry.GetSchemaHash(),
            };

            if (!MasterMemoryDebugBuild.IsEnabled)
            {
                result.Status = MasterDataPatchApplyStatus.Disabled;
                return result;
            }

            var plan = MasterDataPatchEngine.Build(
                patch,
                replaceExisting: replaceExisting,
                requireOriginalPreconditions: true,
                forceIdentity: force);
            result.Warnings.AddRange(plan.Warnings);

            if (!plan.Succeeded)
            {
                foreach (var error in plan.Errors)
                    result.Errors.Add($"{error.Code}: {error.TableName} {error.Key} {error.Field} {error.Message}".Trim());
                var code = plan.Errors.Count == 0 ? null : plan.Errors[0].Code;
                result.Status = code == "UNSUPPORTED_FORMAT" ? MasterDataPatchApplyStatus.UnsupportedFormat
                    : code == "VERSION_MISMATCH" ? MasterDataPatchApplyStatus.VersionMismatch
                    : code == "SCHEMA_MISMATCH" ? MasterDataPatchApplyStatus.SchemaMismatch
                    : MasterDataPatchApplyStatus.Invalid;
                return result;
            }

            try
            {
                MasterDataPatchEngine.Commit(plan);
            }
            catch (InvalidOperationException e)
            {
                result.Status = MasterDataPatchApplyStatus.Conflict;
                result.Errors.Add(e.Message);
                return result;
            }

            result.AppliedRecords = plan.Targets.Count;
            result.AppliedFields = plan.FieldCount;
            foreach (var target in plan.Targets)
            {
                if (target.Kind == MasterDataPatchOperationKind.Add) result.AddedRecords++;
                else if (target.Kind == MasterDataPatchOperationKind.Delete) result.DeletedRecords++;
            }
            result.Status = MasterDataPatchApplyStatus.Applied;
            return result;
        }

        public static bool IsSameMasterVersion(string a, string b)
        {
            return string.Equals(
                string.IsNullOrEmpty(a) ? MasterMemoryDebugRegistry.UnknownMasterVersion : a,
                string.IsNullOrEmpty(b) ? MasterMemoryDebugRegistry.UnknownMasterVersion : b,
                StringComparison.Ordinal);
        }

        internal static MasterMemoryTableDescriptor FindTable(MasterDataPatchTable patchTable)
        {
            if (patchTable.TableName != null && MasterMemoryDebugRegistry.TryGetTable(patchTable.TableName, out var table)) return table;
            foreach (var t in MasterMemoryDebugRegistry.Tables)
            {
                if (patchTable.RecordType != null && t.RecordType.FullName == patchTable.RecordType) return t;
            }
            return null;
        }

        internal static void ApplyTable(MasterDataPatchTable patchTable, MasterMemoryTableDescriptor table, MasterDataPatchApplyResult result)
        {
            var originals = BuildOriginalLookupByKeyJson(table);
            var store = MasterMemoryDebugRuntime.Store;

            foreach (var patchRecord in patchTable.Records)
            {
                string keyText;
                try
                {
                    keyText = NormalizePrimaryKeyJson(table, patchRecord.PrimaryKey);
                }
                catch (Exception e)
                {
                    result.Warnings.Add($"{table.TableName}: invalid primary key {patchRecord.PrimaryKey.ToCanonicalString()} ({e.Message}), skipped.");
                    continue;
                }

                var hasOriginal = originals.TryGetValue(keyText, out var original);
                if (patchRecord.Deleted)
                {
                    if (!hasOriginal)
                    {
                        result.Warnings.Add($"{table.TableName} {keyText}: the deleted record does not exist, skipped.");
                        continue;
                    }
                    store.Delete(table.RecordType, table.GetPrimaryKey(original));
                    result.AppliedRecords++;
                    result.DeletedRecords++;
                    continue;
                }
                if (!hasOriginal)
                {
                    if (!patchRecord.Added)
                    {
                        result.Warnings.Add($"{table.TableName} {keyText}: record does not exist, skipped.");
                        continue;
                    }
                    ApplyAddedRecord(patchRecord, table, keyText, result);
                    continue;
                }
                if (patchRecord.Added)
                {
                    result.Warnings.Add($"{table.TableName} {keyText}: the added record exists in the master data now; its values are applied as changes.");
                }

                var copy = MasterDataCloneUtility.Clone(original);
                var applied = 0;
                foreach (var change in patchRecord.Changes)
                {
                    if (!table.TypeDescriptor.TryGetField(change.Field, out var field))
                    {
                        result.Warnings.Add($"{table.TableName} {keyText}: field '{change.Field}' does not exist, skipped.");
                        continue;
                    }
                    if (!field.CanEdit)
                    {
                        result.Warnings.Add($"{table.TableName} {keyText}: field '{change.Field}' is read-only, skipped.");
                        continue;
                    }
                    try
                    {
                        // nested objects are read into a copy of the original value (members missing in the patch are kept)
                        var currentOriginal = field.GetValue(original);
                        var value = MasterDataValueUtility.FromJson(change.Value, field.FieldType, currentOriginal);
                        if (change.HasOriginal)
                        {
                            var patchOriginal = TryFromJson(change.Original, field.FieldType, currentOriginal, out var ok);
                            if (ok && !MasterDataValueUtility.AreEqual(patchOriginal, currentOriginal))
                            {
                                result.Warnings.Add(
                                    $"{table.TableName} {keyText}.{field.Name}: original value changed in master data " +
                                    $"(patch: {MasterDataValueUtility.Format(patchOriginal)}, current: {MasterDataValueUtility.Format(currentOriginal)}).");
                            }
                        }
                        field.SetValue(copy, value);
                        applied++;
                    }
                    catch (Exception e)
                    {
                        result.Warnings.Add($"{table.TableName} {keyText}: field '{change.Field}' has an invalid value ({e.Message}), skipped.");
                    }
                }

                if (applied > 0)
                {
                    store.Set(table.RecordType, table.GetPrimaryKey(original), copy);
                    result.AppliedRecords++;
                    result.AppliedFields += applied;
                }
            }
        }

        static void ApplyAddedRecord(MasterDataPatchRecord patchRecord, MasterMemoryTableDescriptor table, string keyText, MasterDataPatchApplyResult result)
        {
            if (!MasterMemoryRecordFactory.CanAdd(table, out var reason))
            {
                result.Warnings.Add($"{table.TableName} {keyText}: the added record is skipped ({reason})");
                return;
            }

            object record;
            try
            {
                record = MasterMemoryRecordFactory.CreateDefault(table);
                foreach (var field in table.TypeDescriptor.PrimaryKeyFields)
                {
                    field.SetValueUnchecked(record, MasterDataValueUtility.FromJson(patchRecord.PrimaryKey[field.Name], field.FieldType));
                }
            }
            catch (Exception e)
            {
                result.Warnings.Add($"{table.TableName} {keyText}: the added record can not be created ({(e.InnerException ?? e).Message}), skipped.");
                return;
            }

            var applied = 0;
            foreach (var change in patchRecord.Changes)
            {
                // an added record also sets its secondary keys (a rebuilt database indexes them)
                if (!table.TypeDescriptor.TryGetField(change.Field, out var field) || field.IsPrimaryKey || !field.HasSetter)
                {
                    result.Warnings.Add($"{table.TableName} {keyText} (added): field '{change.Field}' does not exist or can not be written, skipped.");
                    continue;
                }
                try
                {
                    field.SetValueUnchecked(record, MasterDataValueUtility.FromJson(change.Value, field.FieldType, field.GetValue(record)));
                    applied++;
                }
                catch (Exception e)
                {
                    result.Warnings.Add($"{table.TableName} {keyText} (added): field '{change.Field}' has an invalid value ({(e.InnerException ?? e).Message}), skipped.");
                }
            }

            MasterMemoryDebugRuntime.Store.Set(table.RecordType, table.GetPrimaryKey(record), record);
            result.AppliedRecords++;
            result.AddedRecords++;
            result.AppliedFields += applied;
        }

        static object TryFromJson(object json, Type type, object baseValue, out bool ok)
        {
            try
            {
                ok = true;
                return MasterDataValueUtility.FromJson(json, type, baseValue);
            }
            catch (Exception)
            {
                ok = false;
                return null;
            }
        }

        static Dictionary<object, object> BuildOriginalLookupByKey(MasterMemoryTableDescriptor table)
        {
            var lookup = new Dictionary<object, object>();
            foreach (var record in table.GetAllRecords())
            {
                if (record == null) continue;
                lookup[table.GetPrimaryKey(record)] = record;
            }
            return lookup;
        }

        internal static Dictionary<string, object> BuildOriginalLookupByKeyJson(MasterMemoryTableDescriptor table)
        {
            var lookup = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var record in table.GetAllRecords())
            {
                if (record == null) continue;
                lookup[CreatePrimaryKeyJson(table, record).ToCanonicalString()] = record;
            }
            return lookup;
        }

        /// <summary>Re-reads key values with the member types so that e.g. 1001.0 and 1001 match.</summary>
        internal static string NormalizePrimaryKeyJson(MasterMemoryTableDescriptor table, MasterDataJsonObject key)
        {
            var keyFields = table.TypeDescriptor.PrimaryKeyFields;
            if (keyFields.Count == 0) return key.ToCanonicalString();

            var normalized = new MasterDataJsonObject();
            foreach (var field in keyFields)
            {
                if (!key.TryGetValue(field.Name, out var json)) throw new FormatException($"'{field.Name}' is missing");
                normalized.Add(field.Name, MasterDataValueUtility.ToJson(MasterDataValueUtility.FromJson(json, field.FieldType)));
            }
            return normalized.ToCanonicalString();
        }
    }
}
