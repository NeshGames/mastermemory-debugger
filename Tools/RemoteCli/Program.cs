using System.Net.Sockets;

return await RemoteCli.Run(args);

internal static class RemoteCli
{
    public static async Task<int> Run(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            CliPatchCommands.ValidateFiles(options);

            using var connection = await CliConnection.Connect(options);
            var welcome = connection.Welcome;
            object result = options.Command switch
            {
                "inspect" => CliQueryCommands.Inspect(welcome),
                "tables" => CliQueryCommands.Tables(welcome),
                "records" => CliQueryCommands.Records(connection.Stream, welcome, options),
                "changes" => CliQueryCommands.Changes(welcome, options),
                "operations" => CliOperationCommands.List(welcome),
                "validate" => CliQueryCommands.Validate(connection.Stream),
                "invoke" => CliOperationCommands.Invoke(connection.Stream, welcome, options),
                "patch-export" or "patch-plan" or "patch-apply" =>
                    CliPatchCommands.Execute(connection.Stream, welcome, options),
                _ => throw new CliError("USAGE", "Unknown command.", 2),
            };
            CliOutput.Emit(true, result, null);
            return 0;
        }
        catch (CliError e)
        {
            CliOutput.Emit(false, null, new { code = e.Code, message = e.Message, details = e.Details });
            return e.ExitCode;
        }
        catch (OperationCanceledException)
        {
            CliOutput.Emit(false, null, new { code = "TIMEOUT", message = "Connection timed out." });
            return 8;
        }
        catch (IOException e) when (e.InnerException is SocketException socket
            && socket.SocketErrorCode == SocketError.TimedOut)
        {
            CliOutput.Emit(false, null, new { code = "TIMEOUT", message = e.Message });
            return 8;
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException)
        {
            CliOutput.Emit(false, null, new { code = "PROTOCOL_ERROR", message = e.Message });
            return 5;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            CliOutput.Emit(false, null, new { code = "FILE_ERROR", message = e.Message });
            return 2;
        }
        catch (Exception e) when (e is SocketException or IOException)
        {
            CliOutput.Emit(false, null, new { code = "CONNECTION_ERROR", message = e.Message });
            return 3;
        }
        catch (Exception e)
        {
            CliOutput.Emit(false, null, new { code = "INTERNAL_ERROR", message = e.Message });
            return 9;
        }
    }
}
