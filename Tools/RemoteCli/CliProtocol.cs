using Nesh.MasterMemoryDebugger;

internal static class CliProtocol
{
    public static byte[] Read(Stream stream) =>
        MasterMemoryRemoteProtocol.ReadFrame(stream)
        ?? throw new EndOfStreamException("Connection closed before a response arrived.");

    public static MasterMemoryRemoteProtocol.PatchResponse ReadPatchResponse(Stream stream, string requestId)
    {
        for (var i = 0; i < 100; i++)
        {
            var payload = Read(stream);
            if (MasterMemoryRemoteProtocol.GetType(payload) != MasterMemoryRemoteProtocol.MessageType.PatchResponse)
                continue;
            var response = MasterMemoryRemoteProtocol.DecodePatchResponse(payload);
            if (response.RequestId == requestId) return response;
        }
        throw new CliError("PROTOCOL_ERROR", "No matching Patch response received.", 5);
    }

    public static MasterMemoryRemoteProtocol.OperationResult ReadOperationResult(Stream stream, string requestId)
    {
        for (var i = 0; i < 100; i++)
        {
            var payload = Read(stream);
            if (MasterMemoryRemoteProtocol.GetType(payload) != MasterMemoryRemoteProtocol.MessageType.OperationResult)
                continue;
            var result = MasterMemoryRemoteProtocol.DecodeOperationResult(payload);
            if (result.RequestId == requestId) return result;
        }
        throw new CliError("PROTOCOL_ERROR", "No matching operation result received.", 5);
    }

    public static List<MasterMemoryRemoteProtocol.Failure> ReadValidation(Stream stream)
    {
        for (var i = 0; i < 100; i++)
        {
            var payload = Read(stream);
            if (MasterMemoryRemoteProtocol.GetType(payload) == MasterMemoryRemoteProtocol.MessageType.ValidateResult)
                return MasterMemoryRemoteProtocol.DecodeValidateResult(payload);
        }
        throw new CliError("PROTOCOL_ERROR", "No validation result received.", 5);
    }

    public static MasterMemoryRemoteProtocol.Table ReadTable(
        Stream stream,
        MasterMemoryRemoteProtocol.Table manifest,
        string requestId)
    {
        var result = new MasterMemoryRemoteProtocol.Table
        {
            TableName = manifest.TableName,
            MemoryTableName = manifest.MemoryTableName,
            RecordType = manifest.RecordType,
            KeyType = manifest.KeyType,
            Group = manifest.Group,
            RecordCount = manifest.RecordCount,
            HasCustomDisplayName = manifest.HasCustomDisplayName,
            DisplayNames = manifest.HasCustomDisplayName ? new List<string>() : null,
        };

        var nextChunk = 0;
        for (var i = 0; i < 100000; i++)
        {
            var payload = Read(stream);
            if (MasterMemoryRemoteProtocol.GetType(payload) != MasterMemoryRemoteProtocol.MessageType.TableChunk)
                continue;
            var chunk = MasterMemoryRemoteProtocol.DecodeTableChunk(payload);
            if (chunk.RequestId != requestId || chunk.TableName != manifest.TableName) continue;
            if (!string.IsNullOrEmpty(chunk.Error))
                throw new CliError("TABLE_ERROR", chunk.Error, 7);
            if (chunk.ChunkIndex != nextChunk++)
                throw new CliError("PROTOCOL_ERROR", "Table chunks arrived out of order.", 5);

            result.Records.AddRange(chunk.Records);
            if (result.DisplayNames != null && chunk.DisplayNames != null)
                result.DisplayNames.AddRange(chunk.DisplayNames);

            if (!chunk.IsLast) continue;
            if (result.Records.Count != manifest.RecordCount)
                throw new CliError("PROTOCOL_ERROR",
                    $"Expected {manifest.RecordCount} records, received {result.Records.Count}.", 5);
            return result;
        }
        throw new CliError("PROTOCOL_ERROR", "Table transfer did not finish.", 5);
    }
}
