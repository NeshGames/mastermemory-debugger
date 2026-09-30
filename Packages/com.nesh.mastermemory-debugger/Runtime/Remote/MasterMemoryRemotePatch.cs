using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Remote protocol wrapper around <see cref="MasterDataPatchEngine"/>. This layer owns request identity,
    /// state fingerprints and transport DTOs; Patch validation and mutation semantics live in the shared engine.
    /// </summary>
    internal static class MasterMemoryRemotePatch
    {
        static readonly List<MasterDataOverrideStore.AtomicChange> s_emptyChanges =
            new List<MasterDataOverrideStore.AtomicChange>();

        internal sealed class Plan
        {
            public MasterMemoryRemoteProtocol.PatchResponse Response;
            public MasterDataPatchPlan CorePlan;
            public readonly List<string> AfterShas = new List<string>();

            public List<MasterDataOverrideStore.AtomicChange> Changes =>
                CorePlan?.Changes ?? s_emptyChanges;

            public int FieldCount => CorePlan?.FieldCount ?? 0;
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
                foreach (var warning in warnings)
                    Error(response, "", "", "", "EXPORT_WARNING", warning);
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
            try
            {
                patch = MasterDataPatchSerializer.FromJson(request.PatchJson);
            }
            catch (Exception e)
            {
                response.Status = MasterMemoryRemoteProtocol.PatchStatus.Invalid;
                Error(response, "", "", "", "INVALID_PATCH", e.Message);
                return plan;
            }

            var core = MasterDataPatchEngine.Build(
                patch,
                replaceExisting: false,
                requireOriginalPreconditions: true,
                forceIdentity: false);
            plan.CorePlan = core;
            foreach (var error in core.Errors)
                Error(response, error.TableName, error.Key, error.Field, error.Code, error.Message);
            foreach (var warning in core.Warnings)
                Error(response, "", "", "", "PATCH_WARNING", warning);

            if (!core.Succeeded)
            {
                response.Status = MasterMemoryRemoteProtocol.PatchStatus.Invalid;
                response.Message = "No changes applied; patch preflight failed.";
                return plan;
            }

            foreach (var target in core.Targets)
            {
                var beforeSha = BeforeStateSha(target);
                response.Targets.Add(new MasterMemoryRemoteProtocol.PatchTarget
                {
                    TableName = target.Table.TableName,
                    Key = target.KeyText,
                    BeforeSha = beforeSha,
                });
                plan.AfterShas.Add(AfterStateSha(target));
            }

            var digest = new StringBuilder();
            AppendPart(digest, response.PatchSha);
            AppendPart(digest, epoch);
            AppendPart(digest, response.MasterVersion);
            AppendPart(digest, MasterMemoryDebugRegistry.GetSchemaHash());
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

            try
            {
                MasterDataPatchEngine.Commit(plan.CorePlan);
            }
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

            response.AppliedRecords = plan.CorePlan.Targets.Count;
            response.AppliedFields = plan.CorePlan.FieldCount;
            try
            {
                var digest = new StringBuilder();
                for (var i = 0; i < plan.CorePlan.Targets.Count; i++)
                {
                    var target = plan.CorePlan.Targets[i];
                    var actual = CurrentStateSha(target.Change);
                    if (actual != plan.AfterShas[i])
                    {
                        Error(response, target.Table.TableName, target.KeyText, "",
                            "POST_COMMIT_CHANGED", "Target changed after the Patch commit.");
                    }
                    digest.Append(actual).Append('|');
                }
                response.StateSha = Sha(Encoding.UTF8.GetBytes(digest.ToString()));
            }
            catch (Exception e)
            {
                Error(response, "", "", "", "STATE_HASH_ERROR", e.Message);
            }

            try
            {
                response.Failures = MasterMemoryRemoteServer.Validate();
            }
            catch (Exception e)
            {
                Error(response, "", "", "", "VALIDATION_ERROR", e.Message);
            }

            response.Message = "Patch applied.";
            return response;
        }

        static string BeforeStateSha(MasterDataPatchPlanTarget target)
        {
            if (target.WasDeleted) return ShaText("deleted");
            if (target.HadOverride)
                return ShaText("override:" + Sha(MasterMemoryRemotePeer.Serialize(target.Table.RecordType, target.ExpectedOverride)));
            if (target.Original != null)
                return ShaText("original:" + Sha(MasterMemoryRemotePeer.Serialize(target.Table.RecordType, target.Original)));
            return ShaText("absent");
        }

        static string AfterStateSha(MasterDataPatchPlanTarget target)
        {
            if (target.NextOverride == null)
            {
                if (target.Original == null) return ShaText("absent");
                return ShaText("original:" + Sha(MasterMemoryRemotePeer.Serialize(target.Table.RecordType, target.Original)));
            }
            if (MasterDataOverrideStore.IsDeletedValue(target.NextOverride)) return ShaText("deleted");
            return ShaText("override:" + Sha(MasterMemoryRemotePeer.Serialize(target.Table.RecordType, target.NextOverride)));
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

        static MasterMemoryRemoteProtocol.PatchResponse NewResponse(string requestId, string epoch) =>
            new MasterMemoryRemoteProtocol.PatchResponse
            {
                RequestId = requestId,
                ServerEpoch = epoch,
                MasterVersion = MasterMemoryDebugRegistry.GetMasterVersion(),
            };

        static void Error(
            MasterMemoryRemoteProtocol.PatchResponse response,
            string table,
            string key,
            string field,
            string code,
            string message)
        {
            response.Errors.Add(new MasterMemoryRemoteProtocol.PatchError
            {
                TableName = table,
                Key = key,
                Field = field,
                Code = code,
                Message = message,
            });
        }

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
