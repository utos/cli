using System.CommandLine;
using Grpc.Core;
using Utos.Cli.Core.Daemon;
using Utos.Daemon.V1;

namespace Utos.Cli.Commands;

/// <summary>
/// <c>utos logs</c> — stream an execution's events.
/// <para>
/// <c>WatchExecution</c> carries a status only on transitions and an error only on the transition
/// to FAILED, so following a run is a subscription rather than a poll, and the terminal status
/// arrives as part of the stream.
/// </para>
/// </summary>
internal static class LogsCommand
{
    public static Command Create()
    {
        var id = new Argument<string>("execution-id") { Description = "The execution to watch." };
        var follow = new Option<bool>("--follow", "-f") { Description = "Keep streaming until the execution ends." };
        var tail = new Option<int?>("--tail") { Description = "Start from the last N events." };
        var after = new Option<int?>("--after") { Description = "Start after event index N." };
        var source = new Option<string?>("--source") { Description = "Only events from this source." };
        var category = new Option<string?>("--category") { Description = "Only events in this category." };
        var level = new Option<string?>("--level") { Description = "Minimum level: trace, debug, info, warn, error, fatal." };

        var options = new DaemonOptions();
        var command = new Command("logs", "Stream an execution's events.")
        {
            id, follow, tail, after, source, category, level,
        };
        options.AddTo(command);

        command.SetAction((parseResult, cancellationToken) => DaemonOptions.Guard(async () =>
        {
            var request = new WatchExecutionRequest { ExecutionId = parseResult.GetValue(id)! };

            if (parseResult.GetValue(tail) is { } tailCount) request.Tail = tailCount;
            else if (parseResult.GetValue(after) is { } afterIndex) request.After = afterIndex;

            if (parseResult.GetValue(source) is { } sourceFilter) request.Source = sourceFilter;
            if (parseResult.GetValue(category) is { } categoryFilter) request.Category = categoryFilter;
            if (parseResult.GetValue(level) is { } levelFilter) request.Level = ParseLevel(levelFilter);

            using var daemon = options.Connect(parseResult);
            return await Stream(daemon, request, parseResult.GetValue(follow), cancellationToken);
        }));

        return command;
    }

    /// <summary>
    /// Consumes the event stream, returning an exit code that reflects how the execution ended.
    /// </summary>
    public static async Task<int> Stream(DaemonConnection daemon, WatchExecutionRequest request,
        bool follow, CancellationToken cancellationToken)
    {
        using var call = daemon.Observability.WatchExecution(request, cancellationToken: cancellationToken);
        var outcome = ExitCodes.Success;

        try
        {
            await foreach (var e in call.ResponseStream.ReadAllAsync(cancellationToken))
            {
                Write(e);

                if (!e.HasStatus) continue;

                if (e.Status == ExecutionStatus.Failed)
                {
                    if (e.Error is not null)
                    {
                        Output.ErrorLine(Output.Red($"{e.Error.Code}: {e.Error.Message}"));
                    }

                    outcome = ExitCodes.WorkflowFailed;
                }

                // Terminal statuses end the stream; without --follow, stop at the first transition.
                if (e.Status is ExecutionStatus.Completed or ExecutionStatus.Failed) break;
                if (!follow) break;
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl-C while following is how this command is meant to end.
        }

        return outcome;
    }

    private static void Write(WatchExecutionResponse e)
    {
        var time = e.Timestamp?.ToDateTimeOffset().ToLocalTime().ToString("HH:mm:ss") ?? "--:--:--";
        var level = e.Level switch
        {
            LogLevel.Error or LogLevel.Fatal => Output.Red(e.Level.ToString().ToUpperInvariant()),
            LogLevel.Warn => Output.Yellow("WARN"),
            LogLevel.Unspecified => "     ",
            _ => e.Level.ToString().ToUpperInvariant(),
        };

        var scope = e.HasCategory ? $"{e.Source}/{e.Category}" : e.Source;
        Output.Line($"{Output.Dim(time)} {level,-5} {Output.Dim(scope)}  {e.Message}");
    }

    private static LogLevel ParseLevel(string value) => value.ToLowerInvariant() switch
    {
        "trace" => LogLevel.Trace,
        "debug" => LogLevel.Debug,
        "info" => LogLevel.Info,
        "warn" or "warning" => LogLevel.Warn,
        "error" => LogLevel.Error,
        "fatal" => LogLevel.Fatal,
        _ => throw new ArgumentException(
            $"--level '{value}' is not one of trace, debug, info, warn, error, fatal."),
    };
}
