using System.CommandLine;
using Google.Protobuf;
using Grpc.Core;
using Utos.Cli.Core.Rendering;
using Utos.Daemon.V1;
using Utos.Workflows.V1;

namespace Utos.Cli.Commands;

/// <summary>
/// <c>utos execution ls|output</c> — the runs a daemon holds, and what each one produced.
/// <para>
/// <c>output</c> reads the execution's output stream, which is durable: it outlives the in-memory
/// log buffer <c>utos logs</c> follows, so a run's result is still readable after its logs have
/// been evicted.
/// </para>
/// </summary>
internal static class ExecutionCommand
{
    public static Command Create() =>
        new("execution", "List executions and read what they produced.")
        {
            List(),
            ShowOutput(),
        };

    private static Command List()
    {
        var workflow = new Option<string?>("--workflow")
        {
            Description = "Only executions of this workflow, e.g. acme/refund-request or acme/refund-request:1.0.0.",
        };

        var options = new DaemonOptions();
        var command = new Command("ls", "List executions, newest first.") { workflow };
        options.AddTo(command);

        command.SetAction((parseResult, cancellationToken) => DaemonOptions.Guard(async () =>
        {
            var request = new ListExecutionsRequest();
            if (parseResult.GetValue(workflow) is { } filter)
            {
                if (!WorkflowRef.TryParse(filter, out var parsed))
                {
                    Output.ErrorLine(Output.Red($"'{filter}' is not a valid workflow reference."));
                    return ExitCodes.Usage;
                }

                request.Workflow = new WorkflowReference { Name = parsed.Name };
                if (parsed.Namespace is not null) request.Workflow.Namespace = parsed.Namespace;
                if (parsed.Registry is not null) request.Workflow.Registry = parsed.Registry;
                if (parsed.Version is not null) request.Workflow.Version = parsed.Version;
            }

            using var daemon = options.Connect(parseResult);
            var response = await daemon.Executions.ListExecutionsAsync(request, cancellationToken: cancellationToken);

            if (response.Executions.Count == 0)
            {
                Output.Line("No executions. Start one with `utos run <workflow> --start <activity>`.");
                return ExitCodes.Success;
            }

            var rows = response.Executions
                .OrderByDescending(e => e.CreatedAt?.ToDateTimeOffset() ?? DateTimeOffset.MinValue)
                .Select(e => (IReadOnlyList<string>)
                [
                    e.Id,
                    Format.Reference(e.Workflow),
                    Status(e.Status),
                    Output.Dim(e.CreatedAt?.ToDateTimeOffset().ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? ""),
                    e.StartedAt is not null && e.FinishedAt is not null
                        ? Duration.Format(e.FinishedAt.ToDateTimeOffset() - e.StartedAt.ToDateTimeOffset())
                        : "",
                ]);

            foreach (var line in Table.Format(["EXECUTION", "WORKFLOW", "STATUS", "CREATED", "DURATION"], rows, Output.Enabled))
                Output.Line(line);

            return ExitCodes.Success;
        }));

        return command;
    }

    private static Command ShowOutput()
    {
        var id = new Argument<string>("execution-id") { Description = "The execution whose output to show." };

        var options = new DaemonOptions();
        var command = new Command("output", "Show the values and result an execution produced.") { id };
        options.AddTo(command);

        command.SetAction((parseResult, cancellationToken) => DaemonOptions.Guard(async () =>
        {
            using var daemon = options.Connect(parseResult);
            using var call = daemon.Executions.WatchOutput(
                new WatchOutputRequest { ExecutionId = parseResult.GetValue(id)! },
                cancellationToken: cancellationToken);

            var outcome = ExitCodes.Success;
            await foreach (var entry in call.ResponseStream.ReadAllAsync(cancellationToken))
            {
                switch (entry.EntryCase)
                {
                    case WatchOutputResponse.EntryOneofCase.Value:
                        Output.Line($"{Output.Dim("value")}   {Compact.Format(entry.Value)}");
                        break;

                    case WatchOutputResponse.EntryOneofCase.Result:
                        Output.Line(Output.Green("result"));
                        Output.Line(Indented.Format(entry.Result));
                        break;

                    case WatchOutputResponse.EntryOneofCase.Error:
                        Output.ErrorLine(Output.Red($"{entry.Error.Code}: {entry.Error.Message}"));
                        outcome = ExitCodes.WorkflowFailed;
                        break;
                }

                // The terminal entry is always last; nothing follows it.
                if (entry.EntryCase is WatchOutputResponse.EntryOneofCase.Result
                    or WatchOutputResponse.EntryOneofCase.Error)
                    break;
            }

            return outcome;
        }));

        return command;
    }

    private static readonly JsonFormatter Compact = JsonFormatter.Default;
    private static readonly JsonFormatter Indented = new(JsonFormatter.Settings.Default.WithIndentation());

    private static string Status(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Completed => Output.Green("completed"),
        ExecutionStatus.Failed => Output.Red("failed"),
        ExecutionStatus.Cancelled => Output.Yellow("cancelled"),
        ExecutionStatus.Active => Output.Bold("running"),
        ExecutionStatus.Scheduled => Output.Dim("scheduled"),
        _ => Output.Dim("unknown"),
    };
}
