using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Strict, typed patch planning for the remote CLI. All target records are checked before any write.</summary>
    internal static class MasterMemoryRemotePatch
    {
        internal sealed class Plan
        {
            public MasterMemoryRemoteProtocol.PatchResponse Response;
            public readonly List<MasterDataOverrideStore.AtomicChange> Changes = new List<MasterDataOverrideStore.AtomicChange>();
            public readonly List<string> AfterShas = new List<string>();
            public int FieldCount;
        }

        public static MasterMemoryRemoteProtocol.PatchResponse Export(string requestId, string epoch)
        {
            var response = NewResponse(requestId, epoch);
            try
            {
                var warnings = new List<string>();
                var patch = MasterDataPatchService.CreatePatch(warnings);
                response.PatchJson = MasterDataPatchSerializer.ToJson(patch);
                if (Encoding.UTF8.GetByteCount(response.PatchJson) > MasterMemoryRemoteProtocol.MaxPatchJsonBytes)
                    throw new InvalidOperationException("Exported Patch exceeds the 16 MiB remote limit.");
                response.PatchSha = Sha(Encoding.UTF8.GetBytes(response.PatchJson));
                foreach (var warning in warnings) Error(response, "", "", "", "EXPORT_WARNING", warning);
                response.Status = MasterMemoryRemoteProtocol.PatchStatus.Success;
            }
            catch (Exception e)
            {
                response.PatchJson = null;
                response.Status = MasterMemoryRemoteProtocol.PatchStatus.Failed;
                response.Message = e.Message;
            }
            return response;
        }

        public static Plan Build(MasterMemoryRemoteProtocol.PatchRequest request, string epoch)
        {
            var plan = new Plan { Response = NewResponse(request.RequestId, epoch) };
            var response = plan.Response;
            if (request.ServerEpoch != epoch || request.MasterVersion != response.MasterVersion)
            {
                response.Status = MasterMemoryRemoteProtocol.PatchStatus.Stale;
                response.Message = "Server epoch or master version changed.";
                return plan;
            }
            if (string.IsNullOrEmpty(request.PatchJson)
                || Encoding.UTF8.GetByteCount(request.PatchJson) > MasterMemoryRemoteProtocol.MaxPatchJsonBytes)
            {
                response.Status = MasterMemoryRemoteProtocol.PatchStatus.Invalid;
                response.Message = "Patch JSON is empty or exceeds the 16 MiB limit.";
                return plan;
            }
            response.PatchSha = Sha(Encoding.UTF8.GetBytes(request.PatchJson));
            if (!string.IsNullOrEmpty(request.PatchSha) && request.PatchSha != response.PatchSha)
            {
                response.Status = MasterMemoryRemoteProtocol.PatchStatus.Invalid;
                response.Message = "Patch SHA-256 does not match the JSON.";
                return plan;
            }

            MasterDataPatch patch;
            try { patch = MasterDataPatchSerializer.FromJson(request.PatchJson); }
            catch (Exception e)
            {
                response.Status = MasterMemoryRemoteProtocol.PatchStatus.Invalid;
                Error(response, "", "", "", "INVALID_PATCH", e.Message);
                return plan;
            }
            if (patch.FormatVersion <= 0 || patch.FormatVersion > MasterDataPatch.CurrentFormatVersion)
                Error(response, "", "", "", "UNSUPPORTED_FORMAT", "Unsupported patch format version.");
            if (!MasterDataPatchService.IsSameMasterVersion(patch.MasterVersion, response.MasterVersion))
                Error(response, "", "", "", "VERSION_MISMATCH", "Patch master version differs from the game.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var patchTable in patch.Tables)
            {
                if (!MasterMemoryDebugRegistry.TryGetTable(patchTable.TableName, out var table))
                {
                    Error(response, patchTable.TableName, "", "", "TABLE_NOT_FOUND", "Table is not registered.");
                    continue;
                }
                if (!string.IsNullOrEmpty(patchTable.RecordType) && patchTable.RecordType != table.RecordType.FullName)
                {
                    Error(response, patchTable.TableName, "", "", "TYPE_MISMATCH", "Patch record type differs from the table.");
                    continue;
                }
                foreach (var record in patchTable.Records)
                {
                    try { PlanRecord(plan, table, record, seen); }
                    catch (Exception e)
                    {
                        Error(response, table.TableName, record.PrimaryKey?.ToCanonicalString() ?? "", "",
                            "INVALID_RECORD", (e.InnerException ?? e).Message);
                    }
                }
            }
            if (response.Errors.Count != 0)
            {
                response.Status = MasterMemoryRemoteProtocol.PatchStatus.Invalid;
                response.Message = "No changes applied; patch preflight failed.";
                plan.Changes.Clear();
                return plan;
            }
            var digest = new StringBuilder();
            AppendPart(digest, response.PatchSha);
            AppendPart(digest, epoch);
            AppendPart(digest, response.MasterVersion);
            foreach (var target in response.Targets)
            {
                AppendPart(digest, target.TableName);
                AppendPart(digest, target.Key);
                AppendPart(digest, target.BeforeSha);
            }
            response.PlanSha = Sha(Encoding.UTF8.GetBytes(digest.ToString()));
            response.Status = MasterMemoryRemoteProtocol.PatchStatus.Success;
            response.Message = "Patch preflight passed.";
            return plan;
        }

        public static MasterMemoryRemoteProtocol.PatchResponse Apply(Plan plan, string expectedPlanSha)
        {
            var response = plan.Response;
            if (response.Status != MasterMemoryRemoteProtocol.PatchStatus.Success) return response;
            if (string.IsNullOrEmpty(expectedPlanSha) || expectedPlanSha != response.PlanSha)
            {
                response.Status = MasterMemoryRemoteProtocol.PatchStatus.Conflict;
                response.Message = "Patch plan changed; run patch plan again.";
                return response;
            }
            try { ((MasterDataOverrideStore)MasterMemoryDebugRuntime.Store).ApplyAtomic(plan.Changes); }
            catch (InvalidOperationException e)
            {
                response.Status = MasterMemoryRemoteProtocol.PatchStatus.Conflict;
                response.Message = e.Message;
                return response;
            }
            catch (Exception e)
            {
                response.Status = MasterMemoryRemoteProtocol.PatchStatus.Failed;
                response.Message = e.Message;
                return response;
            }

            response.AppliedRecords = plan.Changes.Count;
            response.AppliedFields = plan.FieldCount;
            try
            {
                var digest = new StringBuilder();
                for (var i = 0; i < plan.Changes.Count; i++)
                {
                    var actual = CurrentStateSha(plan.Changes[i]);
                    if (actual != plan.AfterShas[i])
                        Error(response, response.Targets[i].TableName, response.Targets[i].Key, "",
                            "POST_COMMIT_CHANGED", "Target changed after the Patch commit.");
                    digest.Append(actual).Append('|');
                }
                response.StateSha = Sha(Encoding.UTF8.GetBytes(digest.ToString()));
            }
            catch (Exception e) { Error(response, "", "", "", "STATE_HASH_ERROR", e.Message); }
            try { response.Failures = MasterMemoryRemoteServer.Validate(); }
            catch (Exception e) { Error(response, "", "", "", "VALIDATION_ERROR", e.Message); }
            response.Message = "Patch applied.";
            return response;
        }
        static void PlanRecord(Plan plan, MasterMemoryTableDescriptor table, MasterDataPatchRecord patch,
            HashSet<string> seen)
        {
            var response = plan.Response;
            var keyText = MasterDataPatchService.NormalizePrimaryKeyJson(table, patch.PrimaryKey);
            object original = null;
            foreach (var candidate in table.GetAllRecords())
            {
                if (candidate != null && MasterDataPatchService.CreatePrimaryKeyJson(table, candidate).ToCanonicalString() == keyText)
                {
                    original = candidate;
                    break;
                }
            }
            object keyRecord = null;
            if (original == null)
            {
                if (!MasterMemoryRecordFactory.CanAdd(table, out var reason))
                    throw new FormatException(reason);
                keyRecord = MasterMemoryRecordFactory.CreateDefault(table);
                foreach (var field in table.TypeDescriptor.PrimaryKeyFields)
                    field.SetValueUnchecked(keyRecord, MasterDataValueUtility.FromJson(patch.PrimaryKey[field.Name], field.FieldType));
            }
            var key = table.GetPrimaryKey(original ?? keyRecord);
            if (!seen.Add(table.TableName + "|" + keyText))
                throw new FormatException("Duplicate patch target.");
            var hasOriginal = original != null;
            var store = MasterMemoryDebugRuntime.Store;
            var isDeleted = store.IsDeleted(table.RecordType, key);
            var hasOverride = store.TryGet(table.RecordType, key, out var overridden);
            var current = hasOverride ? overridden : original;
            var beforeSha = isDeleted ? ShaText("deleted") : current == null ? ShaText("absent")
                : ShaText((hasOverride ? "override:" : "original:") + Sha(MasterMemoryRemotePeer.Serialize(table.RecordType, current)));
            var errorsBefore = response.Errors.Count;
            object next = null;
            if (patch.Deleted)
            {
                if (!hasOriginal || isDeleted || patch.Changes.Count != 0)
                    Error(response, table.TableName, keyText, "", "INVALID_DELETE", "Delete needs an existing, non-deleted original and no field changes.");
                else next = MasterDataOverrideStore.Deleted;
            }
            else if (patch.Added)
            {
                if (hasOriginal || hasOverride || isDeleted)
                    Error(response, table.TableName, keyText, "", "ALREADY_EXISTS", "Added record already exists.");
                else if (!MasterMemoryRecordFactory.CanAdd(table, out var reason))
                    Error(response, table.TableName, keyText, "", "CANNOT_ADD", reason);
                else
                {
                    next = keyRecord;
                    ApplyFields(response, table, patch, keyText, null, null, next, true);
                }
            }
            else
            {
                if (current == null || isDeleted)
                    Error(response, table.TableName, keyText, "", "NOT_FOUND", "Record does not exist or is deleted.");
                else if (patch.Changes.Count == 0)
                    Error(response, table.TableName, keyText, "", "EMPTY_CHANGE", "Existing record needs at least one field change.");
                else
                {
                    next = MasterDataCloneUtility.Clone(current);
                    ApplyFields(response, table, patch, keyText, original, current, next, false);
                    if (!Equals(table.GetPrimaryKey(next), key))
                        Error(response, table.TableName, keyText, "", "KEY_CHANGED", "Patch changed the primary key.");
                    if (hasOriginal && MasterDataDiffUtility.GetChanges(original, next).Count == 0) next = null;
                }
            }
            if (response.Errors.Count != errorsBefore) return;
            response.Targets.Add(new MasterMemoryRemoteProtocol.PatchTarget
            { TableName = table.TableName, Key = keyText, BeforeSha = beforeSha });
            plan.FieldCount += patch.Changes.Count;
            plan.Changes.Add(new MasterDataOverrideStore.AtomicChange
            { RecordType = table.RecordType, Key = key, Value = next,
                ExpectedValue = isDeleted ? MasterDataOverrideStore.Deleted : hasOverride ? overridden : null });
            plan.AfterShas.Add(next == null ? ShaText("original:" + Sha(MasterMemoryRemotePeer.Serialize(table.RecordType, original)))
                : MasterDataOverrideStore.IsDeletedValue(next) ? ShaText("deleted")
                : ShaText("override:" + Sha(MasterMemoryRemotePeer.Serialize(table.RecordType, next))));
        }

        static string CurrentStateSha(MasterDataOverrideStore.AtomicChange change)
        {
            var store = MasterMemoryDebugRuntime.Store;
            if (store.IsDeleted(change.RecordType, change.Key)) return ShaText("deleted");
            if (store.TryGet(change.RecordType, change.Key, out var overridden))
                return ShaText("override:" + Sha(MasterMemoryRemotePeer.Serialize(change.RecordType, overridden)));
            if (MasterMemoryDebugRegistry.TryGetTable(change.RecordType, out var table)
                && table.TryFindOriginal(change.Key, out var original))
                return ShaText("original:" + Sha(MasterMemoryRemotePeer.Serialize(change.RecordType, original)));
            return ShaText("absent");
        }

        static void ApplyFields(MasterMemoryRemoteProtocol.PatchResponse response, MasterMemoryTableDescriptor table,
            MasterDataPatchRecord patch, string keyText, object original, object current, object next, bool added)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var change in patch.Changes)
            {
                if (!seen.Add(change.Field))
                {
                    Error(response, table.TableName, keyText, change.Field, "DUPLICATE_FIELD", "Field occurs more than once.");
                    continue;
                }
                if (!table.TypeDescriptor.TryGetField(change.Field, out var field)
                    || (added ? field.IsPrimaryKey || !field.HasSetter : field.IsKey || !field.CanEdit))
                {
                    Error(response, table.TableName, keyText, change.Field, "FIELD_NOT_EDITABLE", "Field is unknown or not editable.");
                    continue;
                }
                try
                {
                    var before = added ? field.GetValue(next) : field.GetValue(current);
                    if (!added)
                    {
                        if (!change.HasOriginal)
                        {
                            Error(response, table.TableName, keyText, change.Field, "PRECONDITION_REQUIRED", "Field needs an original value.");
                            continue;
                        }
                        var baseline = field.GetValue(original);
                        var expected = MasterDataValueUtility.FromJson(change.Original, field.FieldType, baseline);
                        if (!MasterDataValueUtility.AreEqual(expected, baseline))
                        {
                            Error(response, table.TableName, keyText, change.Field, "ORIGINAL_MISMATCH", "Master-data original differs from patch original.");
                            continue;
                        }
                    }
                    var value = MasterDataValueUtility.FromJson(change.Value, field.FieldType, before);
                    if (added) field.SetValueUnchecked(next, value);
                    else field.SetValue(next, value);
                }
                catch (Exception e)
                {
                    Error(response, table.TableName, keyText, change.Field, "INVALID_VALUE", (e.InnerException ?? e).Message);
                }
            }
        }

        static MasterMemoryRemoteProtocol.PatchResponse NewResponse(string requestId, string epoch) =>
            new MasterMemoryRemoteProtocol.PatchResponse
            {
                RequestId = requestId, ServerEpoch = epoch,
                MasterVersion = MasterMemoryDebugRegistry.GetMasterVersion(),
            };

        static void Error(MasterMemoryRemoteProtocol.PatchResponse response, string table, string key, string field,
            string code, string message) => response.Errors.Add(new MasterMemoryRemoteProtocol.PatchError
            { TableName = table, Key = key, Field = field, Code = code, Message = message });

        internal static string Fingerprint(MasterMemoryRemoteProtocol.PatchRequest request)
        {
            var value = new StringBuilder();
            AppendPart(value, request.ServerEpoch);
            AppendPart(value, request.MasterVersion);
            AppendPart(value, request.PlanSha);
            AppendPart(value, ShaText(request.PatchJson ?? ""));
            return ShaText(value.ToString());
        }

        static void AppendPart(StringBuilder value, string part)
        {
            part ??= "";
            value.Append(part.Length).Append(':').Append(part);
        }

        static string ShaText(string value) => Sha(Encoding.UTF8.GetBytes(value));

        static string Sha(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
