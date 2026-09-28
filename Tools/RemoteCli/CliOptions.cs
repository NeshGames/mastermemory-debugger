internal sealed class CliOptions
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

    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help")
            throw new CliError("USAGE",
                "Usage: mmdebug <inspect|tables|records|changes|validate|operations|invoke|patch-export|patch-plan|patch-apply> --host HOST --code CODE [--port 7788] [--timeout 30] [--table NAME] [--offset N] [--limit N] [--id ID --epoch EPOCH --context CONTEXT --revision N --request-id ID] [--file PATCH.json --output PATCH.json --plan-sha SHA --master-version VERSION]",
                2);

        var o = new CliOptions
        {
            Command = args[0],
            Code = Environment.GetEnvironmentVariable("MMDEBUG_CODE") ?? "",
        };
        for (var i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length)
                throw new CliError("USAGE", $"Missing value for {args[i]}.", 2);
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
            throw new CliError("USAGE",
                "patch-apply requires --request-id, --epoch, --master-version and --plan-sha.", 2);

        if (!new[] { "inspect", "tables", "records", "changes", "validate", "operations", "invoke",
                "patch-export", "patch-plan", "patch-apply" }.Contains(o.Command))
            throw new CliError("USAGE", $"Unknown command {o.Command}.", 2);
        return o;
    }

    static int Number(string text, int min, int max) =>
        int.TryParse(text, out var value) && value >= min && value <= max
            ? value
            : throw new CliError("USAGE", $"Expected an integer from {min} to {max}: {text}.", 2);
}
