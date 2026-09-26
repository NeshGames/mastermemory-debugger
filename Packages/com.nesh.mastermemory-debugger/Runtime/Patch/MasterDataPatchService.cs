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
        /// <summary>The patch format is not supported. Nothing was applied.</summary>
        UnsupportedFormat,
        /// <summary>The debugger is disabled in this build. Nothing was applied.</summary>
        Disabled,
    }

    public sealed class MasterDataPatchApplyResult
    {
        public MasterDataPatchApplyStatus Status;
        public string PatchMasterVersion;
        public string CurrentMasterVersion;
        public int AppliedRecords;
        public int AppliedFields;
        public readonly List<string> Warnings = new List<string>();

        public bool Succeeded => Status == MasterDataPatchApplyStatus.Applied;
    }

    /// <summary>Creates patches from the current overrides and applies patches to the override store.</summary>
    public static class MasterDataPatchService
    {
        // ------------------------------------------------------------------ create

        /// <summary>
        /// Builds a patch of every override whose record type is registered.
        /// Only editable fields whose value differs from the original are written.
        /// </summary>
        public static MasterDataPatch CreatePatch(List<string> warnings = null)
        {
            var patch = new MasterDataPatch
            {
                MasterVersion = MasterMemoryDebugRegistry.GetMasterVersion(),
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
                    if (!originals.TryGetValue(entry.Key.PrimaryKey, out var original))
                    {
                        warnings?.Add($"{table.TableName} {MasterDataValueUtility.FormatKey(entry.Key.PrimaryKey)}: original record not found, skipped.");
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
            return patch;
        }

        public static string CreatePatchJson(List<string> warnings = null)
        {
            return MasterDataPatchSerializer.ToJson(CreatePatch(warnings));
        }

        /// <summary>Changed fields of one record (editable, non-key, simple fields only).</summary>
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
                record.Changes.Add(new MasterDataPatchChange
                {
                    Field = field.Name,
                    Original = MasterDataValueUtility.ToJson(originalValue),
                    Value = MasterDataValueUtility.ToJson(currentValue),
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
            };

            if (!MasterMemoryDebugBuild.IsEnabled)
            {
                result.Status = MasterDataPatchApplyStatus.Disabled;
                return result;
            }
            if (patch.FormatVersion > MasterDataPatch.CurrentFormatVersion || patch.FormatVersion <= 0)
            {
                result.Status = MasterDataPatchApplyStatus.UnsupportedFormat;
                result.Warnings.Add($"Unsupported patch format version {patch.FormatVersion}.");
                return result;
            }
            if (!IsSameMasterVersion(result.PatchMasterVersion, result.CurrentMasterVersion))
            {
                if (!force)
                {
                    result.Status = MasterDataPatchApplyStatus.VersionMismatch;
                    return result;
                }
                result.Warnings.Add($"Master version differs (patch: {result.PatchMasterVersion}, current: {result.CurrentMasterVersion}). Force loaded.");
            }

            using (MasterMemoryDebugRuntime.BeginBatch())
            {
                if (replaceExisting) MasterMemoryDebugRuntime.Store.Clear();
                foreach (var patchTable in patch.Tables)
                {
                    var table = FindTable(patchTable);
                    if (table == null)
                    {
                        result.Warnings.Add($"Table '{patchTable.TableName}' is not registered, skipped.");
                        continue;
                    }
                    ApplyTable(patchTable, table, result);
                }
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

                if (!originals.TryGetValue(keyText, out var original))
                {
                    result.Warnings.Add($"{table.TableName} {keyText}: record does not exist (records can not be added), skipped.");
                    continue;
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
                        var value = MasterDataValueUtility.FromJson(change.Value, field.FieldType);
                        var currentOriginal = field.GetValue(original);
                        if (change.HasOriginal)
                        {
                            var patchOriginal = TryFromJson(change.Original, field.FieldType, out var ok);
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

        static object TryFromJson(object json, Type type, out bool ok)
        {
            try
            {
                ok = true;
                return MasterDataValueUtility.FromJson(json, type);
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

        static Dictionary<string, object> BuildOriginalLookupByKeyJson(MasterMemoryTableDescriptor table)
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
        static string NormalizePrimaryKeyJson(MasterMemoryTableDescriptor table, MasterDataJsonObject key)
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
