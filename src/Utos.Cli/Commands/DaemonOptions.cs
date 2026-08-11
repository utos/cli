using System.CommandLine;
using Grpc.Core;
using Utos.Cli.Core.Daemon;

namespace Utos.Cli.Commands;

/// <summary>
/// The <c>--host</c> / <c>--context</c> pair every daemon-facing command accepts, plus the
/// translation of gRPC failures into readable messages and exit codes.
/// </summary>
internal sealed class DaemonOptions
{
    public Option<string?> Host { get; } = new("--host")
    {
        Description = "Daemon address, e.g. http://localhost:5164. Overrides the selected context.",
    };

    public Option<string?> Context { get; } = new("--context")
    {
        Description = "Named context to use instead of the current one.",
    };

    public void AddTo(Command command)
    {
        command.Options.Add(Host);
        command.Options.Add(Context);
    }

    public DaemonConnection Connect(ParseResult parseResult) =>
        DaemonConnection.Connect(ContextStore.ResolveHost(parseResult.GetValue(Host), parseResult.GetValue(Context)));

    /// <summary>
    /// Runs <paramref name="action"/>, turning the failures a daemon call can produce into a
    /// message and an exit code. The daemon's status codes carry specific meanings worth keeping:
    /// <c>NotFound</c> for an unknown workflow or execution, <c>InvalidArgument</c> for a rejected
    /// request, <c>FailedPrecondition</c> for a non-terminal execution.
    /// </summary>
    public static async Task<int> Guard(Func<Task<int>> action)
    {
        try
        {
            return await action();
        }
        catch (DaemonConfigurationException ex)
        {
            Output.ErrorLine(Output.Red(ex.Message));
            return ExitCodes.Usage;
        }
        catch (RpcException ex)
        {
            Output.ErrorLine(Output.Red(Describe(ex)));
            return ExitCodes.DaemonError;
        }
        catch (ArgumentException ex)
        {
            Output.ErrorLine(Output.Red(ex.Message));
            return ExitCodes.Usage;
        }
    }

    private static string Describe(RpcException ex) => ex.StatusCode switch
    {
        StatusCode.Unavailable =>
            $"Cannot reach the daemon: {ex.Status.Detail}",
        StatusCode.NotFound =>
            $"Not found: {ex.Status.Detail}",
        StatusCode.InvalidArgument =>
            $"The daemon rejected the request: {ex.Status.Detail}",
        StatusCode.FailedPrecondition =>
            $"Not allowed in the current state: {ex.Status.Detail}",
        StatusCode.DeadlineExceeded =>
            "The daemon did not respond in time.",
        _ => $"Daemon error ({ex.StatusCode}): {ex.Status.Detail}",
    };
}
