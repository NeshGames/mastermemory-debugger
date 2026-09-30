using System.Security.Cryptography;
using System.Text.Json;
using MessagePack;
using Nesh.MasterMemoryDebugger;

internal static class CliQueryCommands
{
    public static object Inspect(MasterMemoryRemoteProtocol.Welcome welcome) =>
        new
        {
            welcome.ServerEpoch,
            welcome.MasterVersion,
            welcome.SchemaHash,
            ProtocolVersion = welcome.Version,
            TableCount = welcome.Tables.Count,
            OverrideCount = welcome.Overrides.Count,
        };

    public static object Tables(MasterMemoryRemoteProtocol.Welcome welcome) =>
        welcome.Tables.Select(t => new
        {
            t.TableName,
            t.MemoryTableName,
            t.RecordType,
            t.KeyType,
            t.Group,
            t.RecordCount,
            t.HasCustomDisplayName,
        }).ToArray();

    public static object Records(Stream stream, MasterMemoryRemoteProtocol.Welcome welcome, CliOptions options)
    {
        var manifest = FindTable(welcome, options.Table!);
        var requestId = Guid.NewGuid().ToString("N");
        MasterMemoryRemoteProtocol.WriteFrame(stream,
            MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.TableRequest
            {
                RequestId = requestId,
                TableName = manifest.TableName,
            }));
        var table = CliProtocol.ReadTable(stream, manifest, requestId);
        return new
        {
            table = table.TableName,
            total = manifest.RecordCount,
            offset = options.Offset,
            records = table.Records.Skip(options.Offset).Take(options.Limit).Select((bytes, index) => new
            {
                index = options.Offset + index,
                sha256 = Sha(bytes),
                value = RecordJson(bytes),
                displayName = table.DisplayNames != null && options.Offset + index < table.DisplayNames.Count
                    ? table.DisplayNames[options.Offset + index]
                    : null,
            }).ToArray(),
        };
    }

    public static object Changes(MasterMemoryRemoteProtocol.Welcome welcome, CliOptions options)
    {
        FindTable(welcome, options.Table!);
        var changes = welcome.Overrides
            .Where(c => c.TableName == options.Table)
            .Skip(options.Offset)
            .Take(options.Limit)
            .Select(c => new
            {
                kind = c.Kind.ToString().ToLowerInvariant(),
                sha256 = Sha(c.Record),
                value = RecordJson(c.Record),
            }).ToArray();
        return new
        {
            table = options.Table,
            total = welcome.Overrides.Count(c => c.TableName == options.Table),
            offset = options.Offset,
            changes,
        };
    }

    public static object Validate(Stream stream)
    {
        MasterMemoryRemoteProtocol.WriteFrame(stream, MasterMemoryRemoteProtocol.EncodeValidateRequest());
        return CliProtocol.ReadValidation(stream)
            .Select(f => new { f.TableName, f.Key, f.Message, f.IsNew })
            .ToArray();
    }

    internal static MasterMemoryRemoteProtocol.Table FindTable(
        MasterMemoryRemoteProtocol.Welcome welcome,
        string name) =>
        welcome.Tables.FirstOrDefault(t => t.TableName == name)
        ?? throw new CliError("NOT_FOUND", $"Table {name} was not found.", 7);

    static object RecordJson(byte[] bytes)
    {
        try
        {
            return JsonDocument.Parse(MessagePackSerializer.ConvertToJson(bytes)).RootElement.Clone();
        }
        catch (Exception e) when (e is MessagePackSerializationException or JsonException or InvalidOperationException)
        {
            throw new CliError("RECORD_DECODE_ERROR", e.Message, 5);
        }
    }

    static string Sha(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
