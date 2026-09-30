using System.Security.Cryptography;
using System.Text;
using Nesh.MasterMemoryDebugger;

internal static class CliPatchCommands
{
    public static void ValidateFiles(CliOptions options)
    {
        if (options.Command is "patch-plan" or "patch-apply")
        {
            if (!File.Exists(options.File))
                throw new CliError("FILE_ERROR", "Patch file was not found.", 2);
            if (new FileInfo(options.File!).Length > MasterMemoryRemoteProtocol.MaxPatchJsonBytes)
                throw new CliError("USAGE", "Patch exceeds the 16 MiB limit.", 2);
        }
        if (options.Command == "patch-export" && File.Exists(options.Output))
            throw new CliError("FILE_ERROR", "Output file already exists.", 2);
    }

    public static object Execute(
        Stream stream,
        MasterMemoryRemoteProtocol.Welcome welcome,
        CliOptions options)
    {
        var type = options.Command switch
        {
            "patch-export" => MasterMemoryRemoteProtocol.MessageType.PatchExportRequest,
            "patch-plan" => MasterMemoryRemoteProtocol.MessageType.PatchPlanRequest,
            _ => MasterMemoryRemoteProtocol.MessageType.PatchApplyRequest,
        };

        var json = type == MasterMemoryRemoteProtocol.MessageType.PatchExportRequest
            ? ""
            : ReadPatchFile(options.File!);
        if (Encoding.UTF8.GetByteCount(json) > MasterMemoryRemoteProtocol.MaxPatchJsonBytes)
            throw new CliError("USAGE", "Patch exceeds the 16 MiB limit.", 2);

        var patchSha = type == MasterMemoryRemoteProtocol.MessageType.PatchExportRequest
            ? ""
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

        if (type == MasterMemoryRemoteProtocol.MessageType.PatchApplyRequest
            && options.Epoch != welcome.ServerEpoch)
            throw new CliError("STALE", "Server epoch or master version changed.", 6);

        var requestId = options.RequestId ?? Guid.NewGuid().ToString("N");
        var request = new MasterMemoryRemoteProtocol.PatchRequest
        {
            RequestId = requestId,
            ServerEpoch = options.Epoch ?? welcome.ServerEpoch,
            MasterVersion = options.MasterVersion ?? welcome.MasterVersion,
            PatchSha = patchSha,
            PlanSha = options.PlanSha ?? "",
            PatchJson = json,
        };

        MasterMemoryRemoteProtocol.WriteFrame(stream,
            MasterMemoryRemoteProtocol.EncodePatchRequest(type, request));
        var response = CliProtocol.ReadPatchResponse(stream, requestId);
        if (response.Status != MasterMemoryRemoteProtocol.PatchStatus.Success)
        {
            var code = response.Status switch
            {
                MasterMemoryRemoteProtocol.PatchStatus.Stale => "STALE",
                MasterMemoryRemoteProtocol.PatchStatus.Conflict => "CONFLICT",
                MasterMemoryRemoteProtocol.PatchStatus.Invalid => "PATCH_INVALID",
                _ => "PATCH_FAILED",
            };
            throw new CliError(
                code,
                response.Message,
                code is "STALE" or "CONFLICT" ? 6 : 7,
                response.Errors.Select(e => new
                {
                    e.TableName,
                    e.Key,
                    e.Field,
                    e.Code,
                    e.Message,
                }).ToArray());
        }

        if (type == MasterMemoryRemoteProtocol.MessageType.PatchExportRequest)
            WritePatchFile(options.Output!, response.PatchJson);

        return new
        {
            response.RequestId,
            status = response.Status.ToString(),
            response.ServerEpoch,
            response.MasterVersion,
            response.PatchSha,
            response.PlanSha,
            response.StateSha,
            response.AppliedRecords,
            response.AppliedFields,
            output = type == MasterMemoryRemoteProtocol.MessageType.PatchExportRequest
                ? options.Output
                : null,
            targets = response.Targets.Select(t => new { t.TableName, t.Key, t.BeforeSha }).ToArray(),
            errors = response.Errors.Select(e => new
            {
                e.TableName,
                e.Key,
                e.Field,
                e.Code,
                e.Message,
            }).ToArray(),
            validation = response.Failures.Select(f => new
            {
                f.TableName,
                f.Key,
                f.Message,
                f.IsNew,
            }).ToArray(),
        };
    }

    static string ReadPatchFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new CliError("FILE_ERROR", e.Message, 2);
        }
    }

    static void WritePatchFile(string path, string json)
    {
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(file, new UTF8Encoding(false));
            writer.Write(json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new CliError("FILE_ERROR", e.Message, 2);
        }
    }
}
