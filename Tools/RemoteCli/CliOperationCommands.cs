using Nesh.MasterMemoryDebugger;

internal static class CliOperationCommands
{
    public static object List(MasterMemoryRemoteProtocol.Welcome welcome) =>
        welcome.Operations.Select(o => new
        {
            o.Id,
            o.Label,
            o.Context,
            o.Revision,
            welcome.ServerEpoch,
        }).ToArray();

    public static object Invoke(Stream stream, MasterMemoryRemoteProtocol.Welcome welcome, CliOptions options)
    {
        if (welcome.ServerEpoch != options.Epoch)
            throw new CliError("STALE", "Server epoch changed.", 6);

        var operation = welcome.Operations.FirstOrDefault(x => x.Id == options.Id);
        if (operation == null || operation.Context != options.Context || operation.Revision != options.Revision)
            throw new CliError("STALE", "Operation context or revision changed.", 6);

        var requestId = options.RequestId!;
        if (requestId.Length is < 1 or > 64)
            throw new CliError("USAGE", "--request-id must contain 1 to 64 characters.", 2);

        MasterMemoryRemoteProtocol.WriteFrame(stream,
            MasterMemoryRemoteProtocol.Encode(new MasterMemoryRemoteProtocol.OperationRequest
            {
                RequestId = requestId,
                OperationId = options.Id,
                Context = options.Context,
                Revision = options.Revision!.Value,
            }));

        var result = CliProtocol.ReadOperationResult(stream, requestId);
        var status = (MasterMemoryRemoteOperationStatus)result.Status;
        if (status == MasterMemoryRemoteOperationStatus.Stale)
            throw new CliError("STALE", result.Message, 6);
        if (status is not (MasterMemoryRemoteOperationStatus.Success or MasterMemoryRemoteOperationStatus.NoChange))
            throw new CliError("OPERATION_FAILED", $"{status}: {result.Message}", 7);

        return new
        {
            result.RequestId,
            status = status.ToString(),
            result.Message,
            result.OldSha,
            result.NewSha,
        };
    }
}

// Keep this numeric mapping in step with MasterMemoryDebugRemote.cs without pulling Unity into the CLI.
internal enum MasterMemoryRemoteOperationStatus : byte
{
    Success,
    NoChange,
    Busy,
    Stale,
    Incompatible,
    ValidationError,
    Failed,
}
