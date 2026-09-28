using System.Text.Json;

internal static class CliOutput
{
    const int ContractVersion = 1;
    static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void Emit(bool ok, object? data, object? error) =>
        Console.WriteLine(JsonSerializer.Serialize(
            new { schemaVersion = ContractVersion, ok, data, error },
            JsonOptions));
}
