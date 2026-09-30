internal sealed class CliError : Exception
{
    public CliError(string code, string message, int exitCode, object? details = null)
        : base(message)
    {
        Code = code;
        ExitCode = exitCode;
        Details = details;
    }

    public string Code { get; }
    public int ExitCode { get; }
    public object? Details { get; }
}
