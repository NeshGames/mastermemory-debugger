using System;
using System.Collections.Generic;

namespace Nesh.MasterMemoryDebugger
{
    internal enum MasterDataPatchOperationKind
    {
        Change,
        Add,
        Delete,
        Reset,
    }

    internal sealed class MasterDataPatchPlanError
    {
        public string TableName;
        public string Key;
        public string Field;
        public string Code;
        public string Message;
    }

    internal sealed class MasterDataPatchPlanTarget
    {
        public MasterMemoryTableDescriptor Table;
        public string KeyText;
        public object Key;
        public object Original;
        public object ExpectedOverride;
        public object NextOverride;
        public bool HadOverride;
        public bool WasDeleted;
        public int FieldCount;
        public MasterDataPatchOperationKind Kind;
        public MasterDataOverrideStore.AtomicChange Change;
    }

    /// <summary>
    /// Fully preflighted patch transaction. No store mutation occurs until <see cref="MasterDataPatchEngine.Commit"/>.
    /// </summary>
    internal sealed class MasterDataPatchPlan
    {
        public readonly List<MasterDataPatchPlanTarget> Targets = new List<MasterDataPatchPlanTarget>();
        public readonly List<MasterDataOverrideStore.AtomicChange> Changes = new List<MasterDataOverrideStore.AtomicChange>();
        public readonly List<MasterDataPatchPlanError> Errors = new List<MasterDataPatchPlanError>();
        public readonly List<string> Warnings = new List<string>();
        public int FieldCount;
        public bool Succeeded => Errors.Count == 0;
    }

    /// <summary>
    /// Single source of truth for Patch semantics used by local UI/storage and remote editing.
    /// Planning validates every target first; committing uses the override store's compare-and-swap atomic update.
    /// </summary>
    internal static class MasterDataPatchEngine
    {
        public static MasterDataPatchPlan Build(
            MasterDataPatch patch,
            bool replaceExisting,
            bool requireOriginalPreconditions,
            bool forceIdentity)
        {
            if (patch == null) throw new ArgumentNullException(nameof(patch));
            var plan = new MasterDataPatchPlan();

            if (patch.FormatVersion <= 0 || patch.FormatVersion > MasterDataPatch.CurrentFormatVersion)
                Error(plan, "", "", "", "UNSUPPORTED_FORMAT", "Unsupported patch format version.");

            var currentVersion = MasterMemoryDebugRegistry.GetMasterVersion();
            if (!MasterDataPatchService.IsSameMasterVersion(patch.MasterVersion, currentVersion))
            {
                if (forceIdentity) plan.Warnings.Add(
                    $"Master version differs (patch: {patch.MasterVersion ?? MasterMemoryDebugRegistry.UnknownMasterVersion}, current: {currentVersion}). Force loaded.");
                else Error(plan, "", "", "", "VERSION_MISMATCH", "Patch master version differs from the current master data.");
            }

            var currentSchema = MasterMemoryDebugRegistry.GetSchemaHash();
            if (!string.IsNullOrEmpty(patch.SchemaHash)
                && !string.Equals(patch.SchemaHash, currentSchema, StringComparison.Ordinal))
            {
                if (forceIdentity) plan.Warnings.Add(
                    $"Master schema differs (patch: {patch.SchemaHash}, current: {currentSchema}). Force loaded.");
                else Error(plan, "", "", "", "SCHEMA_MISMATCH", "Patch schema differs from the registered master-data schema.");
            }

            var seen = new HashSet<MasterDataOverrideKey>();
            foreach (var patchTable in patch.Tables)
            {
                var table = MasterDataPatchService.FindTable(patchTable);
                if (table == null)
                {
                    Error(plan, patchTable.TableName, "", "", "TABLE_NOT_FOUND", "Table is not registered.");
                    continue;
                }
                if (!string.IsNullOrEmpty(patchTable.RecordType) && patchTable.RecordType != table.RecordType.FullName)
                {
                    Error(plan, patchTable.TableName, "", "", "TYPE_MISMATCH", "Patch record type differs from the table.");
                    continue;
                }

                var originals = MasterDataPatchService.BuildOriginalLookupByKeyJson(table);
                foreach (var record in patchTable.Records)
                {
                    try
                    {
                        PlanRecord(plan, table, originals, record, seen, replaceExisting, requireOriginalPreconditions);
                    }
                    catch (Exception e)
                    {
                        Error(plan, table.TableName, record.PrimaryKey?.ToCanonicalString() ?? "", "",
                            "INVALID_RECORD", (e.InnerException ?? e).Message);
                    }
                }
            }

            if (replaceExisting)
            {
                foreach (var entry in MasterMemoryDebugRuntime.Store.GetEntries())
                {
                    if (seen.Contains(entry.Key)) continue;
                    plan.Changes.Add(new MasterDataOverrideStore.AtomicChange
                    {
                        RecordType = entry.Key.RecordType,
                        Key = entry.Key.PrimaryKey,
                        Value = null,
                        ExpectedValue = entry.Value,
                    });
                }
            }

            if (plan.Errors.Count != 0)
            {
                plan.Targets.Clear();
                plan.Changes.Clear();
                plan.FieldCount = 0;
            }
            return plan;
        }

        public static void Commit(MasterDataPatchPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!plan.Succeeded) throw new InvalidOperationException("Patch preflight failed.");
            MasterMemoryDebugRuntime.Session.OverrideStore.ApplyAtomic(plan.Changes);
        }

        static void PlanRecord(
            MasterDataPatchPlan plan,
            MasterMemoryTableDescriptor table,
            Dictionary<string, object> originals,
            MasterDataPatchRecord patch,
            HashSet<MasterDataOverrideKey> seen,
            bool replaceExisting,
            bool requireOriginalPreconditions)
        {
            var keyText = MasterDataPatchService.NormalizePrimaryKeyJson(table, patch.PrimaryKey);
            originals.TryGetValue(keyText, out var original);

            object keyRecord = null;
            if (original == null)
            {
                if (!MasterMemoryRecordFactory.CanAdd(table, out var reason))
                    throw new FormatException(reason);
                keyRecord = MasterMemoryRecordFactory.CreateDefault(table);
                foreach (var field in table.TypeDescriptor.PrimaryKeyFields)
                    field.SetValueUnchecked(keyRecord,
                        MasterDataValueUtility.FromJson(patch.PrimaryKey[field.Name], field.FieldType));
            }

            var key = table.GetPrimaryKey(original ?? keyRecord);
            var overrideKey = new MasterDataOverrideKey(table.RecordType, key);
            if (!seen.Add(overrideKey)) throw new FormatException("Duplicate patch target.");

            var store = MasterMemoryDebugRuntime.Store;
            var actualDeleted = store.IsDeleted(table.RecordType, key);
            var actualOverride = store.TryGet(table.RecordType, key, out var overridden);
            var expectedOverride = actualDeleted ? MasterDataOverrideStore.Deleted : actualOverride ? overridden : null;

            // replaceExisting plans against a clean override layer, but still CAS-checks the actual old layer at commit.
            var isDeleted = replaceExisting ? false : actualDeleted;
            var hasOverride = replaceExisting ? false : actualOverride;
            var current = hasOverride ? overridden : original;
            var errorsBefore = plan.Errors.Count;
            object next = null;
            var kind = MasterDataPatchOperationKind.Change;

            if (patch.Deleted)
            {
                kind = MasterDataPatchOperationKind.Delete;
                if (original == null || isDeleted || patch.Changes.Count != 0)
                    Error(plan, table.TableName, keyText, "", "INVALID_DELETE",
                        "Delete needs an existing, non-deleted original and no field changes.");
                else
                    next = MasterDataOverrideStore.Deleted;
            }
            else if (patch.Added)
            {
                kind = MasterDataPatchOperationKind.Add;
                if (original != null || hasOverride || isDeleted)
                    Error(plan, table.TableName, keyText, "", "ALREADY_EXISTS", "Added record already exists.");
                else if (!MasterMemoryRecordFactory.CanAdd(table, out var reason))
                    Error(plan, table.TableName, keyText, "", "CANNOT_ADD", reason);
                else
                {
                    next = keyRecord;
                    ApplyFields(plan, table, patch, keyText, null, null, next, true, requireOriginalPreconditions);
                }
            }
            else
            {
                if (current == null || isDeleted)
                    Error(plan, table.TableName, keyText, "", "NOT_FOUND", "Record does not exist or is deleted.");
                else if (patch.Changes.Count == 0)
                    Error(plan, table.TableName, keyText, "", "EMPTY_CHANGE", "Existing record needs at least one field change.");
                else
                {
                    next = MasterDataCloneUtility.Clone(current);
                    ApplyFields(plan, table, patch, keyText, original, current, next, false, requireOriginalPreconditions);
                    if (!Equals(table.GetPrimaryKey(next), key))
                        Error(plan, table.TableName, keyText, "", "KEY_CHANGED", "Patch changed the primary key.");
                    if (original != null && MasterDataDiffUtility.GetChanges(original, next).Count == 0)
                    {
                        next = null;
                        kind = MasterDataPatchOperationKind.Reset;
                    }
                }
            }

            if (plan.Errors.Count != errorsBefore) return;

            var change = new MasterDataOverrideStore.AtomicChange
            {
                RecordType = table.RecordType,
                Key = key,
                Value = next,
                ExpectedValue = expectedOverride,
            };
            var target = new MasterDataPatchPlanTarget
            {
                Table = table,
                KeyText = keyText,
                Key = key,
                Original = original,
                ExpectedOverride = expectedOverride,
                NextOverride = next,
                HadOverride = actualOverride,
                WasDeleted = actualDeleted,
                FieldCount = patch.Changes.Count,
                Kind = kind,
                Change = change,
            };
            plan.Targets.Add(target);
            plan.Changes.Add(change);
            plan.FieldCount += patch.Changes.Count;
        }

        static void ApplyFields(
            MasterDataPatchPlan plan,
            MasterMemoryTableDescriptor table,
            MasterDataPatchRecord patch,
            string keyText,
            object original,
            object current,
            object next,
            bool added,
            bool requireOriginalPreconditions)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var change in patch.Changes)
            {
                if (!seen.Add(change.Field))
                {
                    Error(plan, table.TableName, keyText, change.Field, "DUPLICATE_FIELD", "Field occurs more than once.");
                    continue;
                }
                if (!table.TypeDescriptor.TryGetField(change.Field, out var field)
                    || (added ? field.IsPrimaryKey || !field.HasSetter : field.IsKey || !field.CanEdit))
                {
                    Error(plan, table.TableName, keyText, change.Field, "FIELD_NOT_EDITABLE", "Field is unknown or not editable.");
                    continue;
                }

                try
                {
                    var before = added ? field.GetValue(next) : field.GetValue(current);
                    if (!added && requireOriginalPreconditions)
                    {
                        if (!change.HasOriginal)
                        {
                            Error(plan, table.TableName, keyText, change.Field, "PRECONDITION_REQUIRED",
                                "Field needs an original value.");
                            continue;
                        }
                        var baseline = field.GetValue(original);
                        var expected = MasterDataValueUtility.FromJson(change.Original, field.FieldType, baseline);
                        if (!MasterDataValueUtility.AreEqual(expected, baseline))
                        {
                            Error(plan, table.TableName, keyText, change.Field, "ORIGINAL_MISMATCH",
                                "Master-data original differs from patch original.");
                            continue;
                        }
                    }
                    else if (!added && change.HasOriginal)
                    {
                        var baseline = field.GetValue(original);
                        var expected = MasterDataValueUtility.FromJson(change.Original, field.FieldType, baseline);
                        if (!MasterDataValueUtility.AreEqual(expected, baseline))
                            plan.Warnings.Add(
                                $"{table.TableName} {keyText}.{field.Name}: original value changed in master data.");
                    }

                    var value = MasterDataValueUtility.FromJson(change.Value, field.FieldType, before);
                    if (added) field.SetValueUnchecked(next, value);
                    else field.SetValue(next, value);
                }
                catch (Exception e)
                {
                    Error(plan, table.TableName, keyText, change.Field, "INVALID_VALUE", (e.InnerException ?? e).Message);
                }
            }
        }

        static void Error(MasterDataPatchPlan plan, string table, string key, string field, string code, string message)
        {
            plan.Errors.Add(new MasterDataPatchPlanError
            {
                TableName = table,
                Key = key,
                Field = field,
                Code = code,
                Message = message,
            });
        }
    }
}
