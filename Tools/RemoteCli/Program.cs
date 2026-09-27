using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using MessagePack;
using Nesh.MasterMemoryDebugger;

return await RemoteCli.Run(args);

internal static class RemoteCli
{
    const int ContractVersion = 1;
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<int> Run(string[] args)
    {
        try
        {
            var options = Parse(args);
            if (options.Command is "patch-plan" or "patch-apply")
            {
                if (!File.Exists(options.File)) throw new CliError("FILE_ERROR", "Patch file was not found.", 2);
                if (new FileInfo(options.File!).Length > MasterMemoryRemoteProtocol.MaxPatchJsonBytes)
                    throw new CliError("USAGE", "Patch exceeds the 16 MiB limit.", 2);
            }
            if (options.Command == "patch-export" && File.Exists(options.Output))
                throw new CliError("FILE_ERROR", "Output file already exists.", 2);
            using var client = new TcpClient();
            using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(options.Host, options.Port, connectTimeout.Token);
            client.ReceiveTimeout = options.Timeout * 1000;
            client.SendTimeout = options.Timeout * 1000;
            using var stream = client.GetStream();
            MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.Hello
            {
                Version = MasterMemoryRemoteProtocol.Version, Code = options.Code,
            }));
            var first = Read(stream);
            if (MasterMemoryRemoteProtocol.GetType(first) == MasterMemoryRemoteProtocol.MessageType.Reject)
                throw new CliError("REJECTED", MasterMemoryRemoteProtocol.DecodeReject(first), 4);
            if (MasterMemoryRemoteProtocol.GetType(first) != MasterMemoryRemoteProtocol.MessageType.Welcome)
                throw new CliError("PROTOCOL_ERROR", "Expected Welcome.", 5);
            var welcome = MasterMemoryRemoteProtocol.DecodeWelcome(first);
            if (welcome.Version != MasterMemoryRemoteProtocol.Version)
                throw new CliError("VERSION_MISMATCH", $"Server protocol {welcome.Version}; CLI protocol {MasterMemoryRemoteProtocol.Version}.", 4);

            object result = options.Command switch
            {
                "inspect" => new { welcome.ServerEpoch, welcome.MasterVersion, ProtocolVersion = welcome.Version,
                    TableCount = welcome.Tables.Count, OverrideCount = welcome.Overrides.Count },
                "tables" => welcome.Tables.Select(t => new { t.TableName, t.MemoryTableName, t.RecordType, t.KeyType,
                    t.Group, RecordCount = t.Records.Count }).ToArray(),
                "records" => Records(welcome, options),
                "changes" => Changes(welcome, options),
                "operations" => welcome.Operations.Select(o => new { o.Id, o.Label, o.Context, o.Revision,
                    welcome.ServerEpoch }).ToArray(),
                "validate" => Validate(stream),
                "invoke" => Invoke(stream, welcome, options),
                "patch-export" or "patch-plan" or "patch-apply" => PatchCall(stream, welcome, options),
                _ => throw new CliError("USAGE", "Unknown command.", 2),
            };
            Emit(true, result, null);
            return 0;
        }
        catch (CliError e) { Emit(false, null, new { code = e.Code, message = e.Message, details = e.Details }); return e.ExitCode; }
        catch (OperationCanceledException) { Emit(false, null, new { code = "TIMEOUT", message = "Connection timed out." }); return 8; }
        catch (IOException e) when (e.InnerException is SocketException socket && socket.SocketErrorCode == SocketError.TimedOut)
        { Emit(false, null, new { code = "TIMEOUT", message = e.Message }); return 8; }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        { Emit(false, null, new { code = "PROTOCOL_ERROR", message = e.Message }); return 5; }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        { Emit(false, null, new { code = "FILE_ERROR", message = e.Message }); return 2; }
        catch (Exception e) when (e is SocketException or IOException)
        { Emit(false, null, new { code = "CONNECTION_ERROR", message = e.Message }); return 3; }
        catch (Exception e)
        { Emit(false, null, new { code = "INTERNAL_ERROR", message = e.Message }); return 9; }
    }

    static object PatchCall(Stream stream, MasterMemoryRemoteProtocol.Welcome welcome, Options options)
    {
        var type = options.Command switch
        {
            "patch-export" => MasterMemoryRemoteProtocol.MessageType.PatchExportRequest,
            "patch-plan" => MasterMemoryRemoteProtocol.MessageType.PatchPlanRequest,
            _ => MasterMemoryRemoteProtocol.MessageType.PatchApplyRequest,
        };
        var json = type == MasterMemoryRemoteProtocol.MessageType.PatchExportRequest ? "" : ReadPatchFile(options.File!);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MasterMemoryRemoteProtocol.MaxPatchJsonBytes)
            throw new CliError("USAGE", "Patch exceeds the 16 MiB limit.", 2);
        var patchSha = type == MasterMemoryRemoteProtocol.MessageType.PatchExportRequest ? ""
            : Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        if (type == MasterMemoryRemoteProtocol.MessageType.PatchApplyRequest
            && (options.Epoch != welcome.ServerEpoch))
            throw new CliError("STALE", "Server epoch or master version changed.", 6);
        var requestId = options.RequestId ?? Guid.NewGuid().ToString("N");
        var request = new MasterMemoryRemoteProtocol.PatchRequest
        {
            RequestId = requestId, ServerEpoch = options.Epoch ?? welcome.ServerEpoch,
            MasterVersion = options.MasterVersion ?? welcome.MasterVersion,
            PatchSha = patchSha, PlanSha = options.PlanSha ?? "", PatchJson = json,
        };
        MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.EncodePatchRequest(type, request));
        for (var i = 0; i < 100; i++)
        {
            var payload = Read(stream);
            if (MasterMemoryRemoteProtocol.GetType(payload) != MasterMemoryRemoteProtocol.MessageType.PatchResponse) continue;
            var response = MasterMemoryRemoteProtocol.DecodePatchResponse(payload);
            if (response.RequestId != requestId) continue;
            if (response.Status != MasterMemoryRemoteProtocol.PatchStatus.Success)
            {
                var code = response.Status switch
                {
                    MasterMemoryRemoteProtocol.PatchStatus.Stale => "STALE",
                    MasterMemoryRemoteProtocol.PatchStatus.Conflict => "CONFLICT",
                    MasterMemoryRemoteProtocol.PatchStatus.Invalid => "PATCH_INVALID",
                    _ => "PATCH_FAILED",
                };
                throw new CliError(code, response.Message, code is "STALE" or "CONFLICT" ? 6 : 7,
                    response.Errors.Select(e => new { e.TableName, e.Key, e.Field, e.Code, e.Message }).ToArray());
            }
            if (type == MasterMemoryRemoteProtocol.MessageType.PatchExportRequest)
            {
                WritePatchFile(options.Output!, response.PatchJson);
            }
            return new
            {
                response.RequestId, status = response.Status.ToString(), response.ServerEpoch,
                response.MasterVersion, response.PatchSha, response.PlanSha, response.StateSha,
                response.AppliedRecords, response.AppliedFields,
                output = type == MasterMemoryRemoteProtocol.MessageType.PatchExportRequest ? options.Output : null,
                targets = response.Targets.Select(t => new { t.TableName, t.Key, t.BeforeSha }).ToArray(),
                errors = response.Errors.Select(e => new { e.TableName, e.Key, e.Field, e.Code, e.Message }).ToArray(),
                validation = response.Failures.Select(f => new { f.TableName, f.Key, f.Message, f.IsNew }).ToArray(),
            };
        }
        throw new CliError("PROTOCOL_ERROR", "No matching Patch response received.", 5);
    }
    static string ReadPatchFile(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new CliError("FILE_ERROR", e.Message, 2); }
    }

    static void WritePatchFile(string path, string json)
    {
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(file, new System.Text.UTF8Encoding(false));
            writer.Write(json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new CliError("FILE_ERROR", e.Message, 2); }
    }
    static Options Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help")
            throw new CliError("USAGE", "Usage: mmdebug <inspect|tables|records|changes|validate|operations|invoke|patch-export|patch-plan|patch-apply> --host HOST --code CODE [--port 7788] [--timeout 30] [--table NAME] [--offset N] [--limit N] [--id ID --epoch EPOCH --context CONTEXT --revision N --request-id ID] [--file PATCH.json --output PATCH.json --plan-sha SHA --master-version VERSION]", 2);
        var o = new Options { Command = args[0], Code = Environment.GetEnvironmentVariable("MMDEBUG_CODE") ?? "" };
        for (var i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length) throw new CliError("USAGE", $"Missing value for {args[i]}.", 2);
            var value = args[i + 1];
            switch (args[i])
            {
                case "--host": o.Host = value; break;
                case "--port": o.Port = Number(value, 1, 65535); break;
                case "--code": o.Code = value; break;
                case "--timeout": o.Timeout = Number(value, 1, 300); break;
                case "--table": o.Table = value; break;
                case "--offset": o.Offset = Number(value, 0, int.MaxValue); break;
                case "--limit": o.Limit = Number(value, 1, 1000); break;
                case "--id": o.Id = value; break;
                case "--epoch": o.Epoch = value; break;
                case "--context": o.Context = value; break;
                case "--revision": o.Revision = Number(value, 0, int.MaxValue); break;
                case "--request-id": o.RequestId = value; break;
                case "--file": o.File = value; break;
                case "--output": o.Output = value; break;
                case "--plan-sha": o.PlanSha = value; break;
                case "--master-version": o.MasterVersion = value; break;
                default: throw new CliError("USAGE", $"Unknown option {args[i]}.", 2);
            }
        }
        if (string.IsNullOrWhiteSpace(o.Host) || string.IsNullOrWhiteSpace(o.Code))
            throw new CliError("USAGE", "--host and --code (or MMDEBUG_CODE) are required.", 2);
        if (o.Command is "records" or "changes" && string.IsNullOrWhiteSpace(o.Table))
            throw new CliError("USAGE", "--table is required.", 2);
        if (o.Command == "invoke" && (string.IsNullOrWhiteSpace(o.Id) || string.IsNullOrWhiteSpace(o.Epoch)
            || o.Context is null || o.Revision is null || string.IsNullOrWhiteSpace(o.RequestId)))
            throw new CliError("USAGE", "invoke requires --id, --epoch, --context, --revision and --request-id.", 2);
        if (o.RequestId != null && (o.RequestId.Length == 0 || o.RequestId.Length > 64))
            throw new CliError("USAGE", "--request-id must contain 1 to 64 characters.", 2);
        if (o.Command == "patch-export" && string.IsNullOrWhiteSpace(o.Output))
            throw new CliError("USAGE", "patch-export requires --output.", 2);
        if (o.Command is "patch-plan" or "patch-apply" && string.IsNullOrWhiteSpace(o.File))
            throw new CliError("USAGE", "patch-plan and patch-apply require --file.", 2);
        if (o.Command == "patch-apply" && (string.IsNullOrWhiteSpace(o.RequestId) || string.IsNullOrWhiteSpace(o.Epoch)
            || string.IsNullOrWhiteSpace(o.MasterVersion) || string.IsNullOrWhiteSpace(o.PlanSha)))
            throw new CliError("USAGE", "patch-apply requires --request-id, --epoch, --master-version and --plan-sha.", 2);
        if (!new[] { "inspect", "tables", "records", "changes", "validate", "operations", "invoke", "patch-export", "patch-plan", "patch-apply" }.Contains(o.Command))
            throw new CliError("USAGE", $"Unknown command {o.Command}.", 2);
        return o;
    }

    static int Number(string text, int min, int max) => int.TryParse(text, out var value) && value >= min && value <= max
        ? value : throw new CliError("USAGE", $"Expected an integer from {min} to {max}: {text}.", 2);

    static object Records(MasterMemoryRemoteProtocol.Welcome w, Options o)
    {
        var table = FindTable(w, o.Table!);
        return new { table = table.TableName, total = table.Records.Count, offset = o.Offset,
            records = table.Records.Skip(o.Offset).Take(o.Limit).Select((bytes, index) => new
            {
                index = o.Offset + index, sha256 = Sha(bytes), value = RecordJson(bytes),
                displayName = table.DisplayNames != null && o.Offset + index < table.DisplayNames.Count
                    ? table.DisplayNames[o.Offset + index] : null,
            }).ToArray() };
    }

    static object Changes(MasterMemoryRemoteProtocol.Welcome w, Options o)
    {
        FindTable(w, o.Table!);
        var changes = w.Overrides.Where(c => c.TableName == o.Table).Skip(o.Offset).Take(o.Limit)
            .Select(c => new { kind = c.Kind.ToString().ToLowerInvariant(), sha256 = Sha(c.Record),
                value = RecordJson(c.Record) }).ToArray();
        return new { table = o.Table, total = w.Overrides.Count(c => c.TableName == o.Table), offset = o.Offset, changes };
    }

    static object Validate(Stream stream)
    {
        MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.EncodeValidateRequest());
        for (var i = 0; i < 100; i++)
        {
            var payload = Read(stream);
            if (MasterMemoryRemoteProtocol.GetType(payload) == MasterMemoryRemoteProtocol.MessageType.ValidateResult)
                return MasterMemoryRemoteProtocol.DecodeValidateResult(payload).Select(f => new
                    { f.TableName, f.Key, f.Message, f.IsNew }).ToArray();
        }
        throw new CliError("PROTOCOL_ERROR", "No validation result received.", 5);
    }

    static object Invoke(Stream stream, MasterMemoryRemoteProtocol.Welcome w, Options o)
    {
        if (w.ServerEpoch != o.Epoch) throw new CliError("STALE", "Server epoch changed.", 6);
        var operation = w.Operations.FirstOrDefault(x => x.Id == o.Id);
        if (operation == null || operation.Context != o.Context || operation.Revision != o.Revision)
            throw new CliError("STALE", "Operation context or revision changed.", 6);
        var requestId = o.RequestId!;
        if (requestId.Length is < 1 or > 64) throw new CliError("USAGE", "--request-id must contain 1 to 64 characters.", 2);
        MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.OperationRequest
        { RequestId = requestId, OperationId = o.Id, Context = o.Context, Revision = o.Revision.Value }));
        for (var i = 0; i < 100; i++)
        {
            var payload = Read(stream);
            if (MasterMemoryRemoteProtocol.GetType(payload) != MasterMemoryRemoteProtocol.MessageType.OperationResult) continue;
            var result = MasterMemoryRemoteProtocol.DecodeOperationResult(payload);
            if (result.RequestId != requestId) continue;
            var status = (MasterMemoryRemoteOperationStatus)result.Status;
            if (status == MasterMemoryRemoteOperationStatus.Stale) throw new CliError("STALE", result.Message, 6);
            if (status is not (MasterMemoryRemoteOperationStatus.Success or MasterMemoryRemoteOperationStatus.NoChange))
                throw new CliError("OPERATION_FAILED", $"{status}: {result.Message}", 7);
            return new { result.RequestId, status = status.ToString(), result.Message, result.OldSha, result.NewSha };
        }
        throw new CliError("PROTOCOL_ERROR", "No matching operation result received.", 5);
    }

    static MasterMemoryRemoteProtocol.Table FindTable(MasterMemoryRemoteProtocol.Welcome w, string name) =>
        w.Tables.FirstOrDefault(t => t.TableName == name)
        ?? throw new CliError("NOT_FOUND", $"Table {name} was not found.", 7);

    static byte[] Read(Stream stream) => MasterMemoryRemoteProtocol.ReadFrame(stream)
        ?? throw new EndOfStreamException("Connection closed before a response arrived.");

    static object RecordJson(byte[] bytes)
    {
        try { return JsonDocument.Parse(MessagePackSerializer.ConvertToJson(bytes)).RootElement.Clone(); }
        catch (Exception e) when (e is MessagePackSerializationException or JsonException or InvalidOperationException)
        { throw new CliError("RECORD_DECODE_ERROR", e.Message, 5); }
    }

    static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static void Emit(bool ok, object? data, object? error) => Console.WriteLine(JsonSerializer.Serialize(
        new { schemaVersion = ContractVersion, ok, data, error }, JsonOptions));

    sealed class Options
    {
        public string Command = "";
        public string Host = "";
        public int Port = 7788;
        public string Code = "";
        public int Timeout = 30;
        public string? Table;
        public int Offset;
        public int Limit = 100;
        public string? Id;
        public string? Epoch;
        public string? Context;
        public int? Revision;
        public string? RequestId;
        public string? File;
        public string? Output;
        public string? PlanSha;
        public string? MasterVersion;
    }

    sealed class CliError(string code, string message, int exitCode, object? details = null) : Exception(message)
    {
        public string Code { get; } = code;
        public int ExitCode { get; } = exitCode;
        public object? Details { get; } = details;
    }
}

// Keep this numeric mapping in step with MasterMemoryDebugRemote.cs without pulling Unity into the CLI.
internal enum MasterMemoryRemoteOperationStatus : byte
{
    Success, NoChange, Busy, Stale, Incompatible, ValidationError, Failed,
}
