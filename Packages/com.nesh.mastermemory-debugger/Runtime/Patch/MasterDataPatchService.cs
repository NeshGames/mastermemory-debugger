using System;
using System.Collections.Generic;
using System.Globalization;

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
            return MasterDataPatchResolver.CreatePrimaryKeyJson(table, record);
        }

        // ------------------------------------------------------------------ preview / apply

        /// <summary>
        /// Builds the same fully validated transaction plan as <see cref="Apply"/> without mutating the override store.
        /// </summary>
        public static MasterDataPatchPreviewResult Preview(
            MasterDataPatch patch,
            bool force = false,
            bool replaceExisting = true)
        {
            if (patch == null) throw new ArgumentNullException(nameof(patch));
            var result = new MasterDataPatchPreviewResult
            {
                PatchMasterVersion = patch.MasterVersion ?? MasterMemoryDebugRegistry.UnknownMasterVersion,
                CurrentMasterVersion = MasterMemoryDebugRegistry.GetMasterVersion(),
                PatchSchemaHash = patch.SchemaHash,
                CurrentSchemaHash = MasterMemoryDebugRegistry.GetSchemaHash(),
            };

            if (!MasterMemoryDebugBuild.IsEnabled)
            {
                result.Status = MasterDataPatchPreviewStatus.Disabled;
                return result;
            }

            var legacyCompatibility = string.IsNullOrEmpty(patch.SchemaHash);
            var plan = MasterDataPatchEngine.Build(
                patch,
                replaceExisting: replaceExisting,
                requireOriginalPreconditions: !legacyCompatibility,
                forceIdentity: force,
                allowPartial: legacyCompatibility);
            result.Warnings.AddRange(plan.Warnings);
            foreach (var error in plan.Errors)
                result.Errors.Add($"{error.Code}: {error.TableName} {error.Key} {error.Field} {error.Message}".Trim());

            if (!plan.Succeeded)
            {
                var code = plan.Errors.Count == 0 ? null : plan.Errors[0].Code;
                result.Status = code == "UNSUPPORTED_FORMAT" ? MasterDataPatchPreviewStatus.UnsupportedFormat
                    : code == "VERSION_MISMATCH" ? MasterDataPatchPreviewStatus.VersionMismatch
                    : code == "SCHEMA_MISMATCH" ? MasterDataPatchPreviewStatus.SchemaMismatch
                    : MasterDataPatchPreviewStatus.Invalid;
                return result;
            }

            result.TargetRecords = plan.Targets.Count;
            result.Fields = plan.FieldCount;
            result.RemovedExistingOverrides = Math.Max(0, plan.Changes.Count - plan.Targets.Count);
            foreach (var target in plan.Targets)
            {
                switch (target.Kind)
                {
                    case MasterDataPatchOperationKind.Add:
                        result.AddedRecords++;
                        break;
                    case MasterDataPatchOperationKind.Delete:
                        result.DeletedRecords++;
                        break;
                    case MasterDataPatchOperationKind.Reset:
                        result.ResetRecords++;
                        break;
                    default:
                        result.ChangedRecords++;
                        break;
                }
            }
            result.Status = MasterDataPatchPreviewStatus.Ready;
            return result;
        }

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

            var legacyCompatibility = string.IsNullOrEmpty(patch.SchemaHash);
            var plan = MasterDataPatchEngine.Build(
                patch,
                replaceExisting: replaceExisting,
                requireOriginalPreconditions: !legacyCompatibility,
                forceIdentity: force,
                allowPartial: legacyCompatibility);
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
            return MasterDataPatchResolver.IsSameMasterVersion(a, b);
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


        /// <summary>Re-reads key values with the member types so that e.g. 1001.0 and 1001 match.</summary>
    }
}
