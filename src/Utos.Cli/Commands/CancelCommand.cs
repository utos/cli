using System.CommandLine;
using Utos.Cli.Core.Daemon;
using Utos.Daemon.V1;

namespace Utos.Cli.Commands;

/// <summary>
/// <c>utos cancel</c> — stop a running execution.
/// <para>
/// Cancellation is terminal and idempotent, and loses to a state the execution already reached:
/// the daemon returns <c>FAILED_PRECONDITION</c> for a run that has already completed or failed,
/// because the first terminal state wins. That surfaces here as an ordinary daemon error rather
/// than being smoothed over — "it finished before you got there" is worth knowing.
/// </para>
/// <para>
/// Cascades to sub-workflows the run is awaiting, whose results can no longer be observed, but not
/// to anything started with <c>workflow.spawn</c>; those are independent executions and are
/// cancelled by their own id.
/// </para>
/// </summary>
internal static class CancelCommand
{
    public static Command Create()
    {
        var id = new Argument<string>("execution-id") { Description = "The execution to cancel." };
        var reason = new Option<string?>("--reason")
        {
            Description = "Why it was cancelled. Recorded on the execution and shown in its summary.",
        };

        var options = new DaemonOptions();
        var command = new Command("cancel", "Stop a running execution.") { id, reason };
        options.AddTo(command);

        command.SetAction((parseResult, cancellationToken) => DaemonOptions.Guard(async () =>
        {
            var request = new CancelExecutionRequest { ExecutionId = parseResult.GetValue(id)! };
            if (parseResult.GetValue(reason) is { } why) request.Reason = why;

            using var daemon = options.Connect(parseResult);
            await daemon.Executions.CancelExecutionAsync(request, cancellationToken: cancellationToken);

            Output.Line($"cancelled {request.ExecutionId}");
            return ExitCodes.Success;
        }));

        return command;
    }
}
