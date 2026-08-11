using System.CommandLine;
using Utos.Cli.Core.Daemon;
using Utos.Daemon.V1;
using Utos.Workflows.V1;

namespace Utos.Cli.Commands;

/// <summary>
/// <c>utos run</c> — schedule an execution.
/// <para>
/// Accepts either a file, in which case it resolves, validates, loads and then schedules — the
/// one-liner for iterating locally, mirroring how <c>docker run</c> pulls when it needs to — or a
/// reference to something already loaded.
/// </para>
/// </summary>
internal static class RunCommand
{
    public static Command Create()
    {
        var target = new Argument<string>("workflow")
        {
            Description = "A workflow file to load and run, or a reference to one already loaded "
                          + "(e.g. acme/greet:1.0.0).",
        };

        var start = new Option<string>("--start")
        {
            Description = "Activity to start at. Required — the spec defines no default.",
            Required = true,
        };

        var input = new Option<string?>("--input")
        {
            Description = "Execution input as a JSON object, or @file.json to read one.",
        };

        var env = new Option<string[]>("--env", "-e")
        {
            Description = "Ambient environment as KEY=VALUE. Repeatable.",
            AllowMultipleArgumentsPerToken = false,
        };

        var envFile = new Option<string?>("--env-file") { Description = "Read KEY=VALUE lines from a file." };
        var follow = new Option<bool>("--follow", "-f")
        {
            Description = "Stream events until the execution ends, and exit non-zero if it fails.",
        };

        var options = new DaemonOptions();
        var command = new Command("run", "Schedule a workflow execution.")
        {
            target, start, input, env, envFile, follow,
        };
        options.AddTo(command);

        command.SetAction((parseResult, cancellationToken) => DaemonOptions.Guard(async () =>
        {
            var payload = PayloadParser.ParseInput(parseResult.GetValue(input));
            var environment = PayloadParser.ParseEnv(
                parseResult.GetValue(env) ?? [], parseResult.GetValue(envFile));

            using var daemon = options.Connect(parseResult);

            var value = parseResult.GetValue(target)!;
            if (!TryResolveReference(value, daemon, out var reference, out var failure)) return failure;

            var request = new ScheduleExecutionRequest
            {
                Workflow = reference,
                Input = payload,
                StartActivity = parseResult.GetValue(start)!,
            };
            foreach (var (key, item) in environment) request.Env[key] = item;

            var scheduled = await daemon.Executions.ScheduleExecutionAsync(request, cancellationToken: cancellationToken);
            Output.Line($"{Output.Green("scheduled")}  {Output.Bold(scheduled.Id)}");

            if (!parseResult.GetValue(follow)) return ExitCodes.Success;

            return await LogsCommand.Stream(daemon,
                new WatchExecutionRequest { ExecutionId = scheduled.Id }, follow: true, cancellationToken);
        }));

        return command;
    }

    /// <summary>
    /// Works out whether the argument names a file or an already-loaded workflow, loading it first
    /// in the former case.
    /// </summary>
    private static bool TryResolveReference(string value, DaemonConnection daemon,
        out WorkflowReference reference, out int failure)
    {
        reference = new WorkflowReference();
        failure = ExitCodes.Success;

        if (LooksLikeFile(value))
        {
            if (!File.Exists(value))
            {
                Output.ErrorLine(Output.Red($"'{value}' does not exist."));
                failure = ExitCodes.Usage;
                return false;
            }

            if (!Pipeline.TryResolve(value, out var built, out failure)) return false;

            var loaded = daemon.Definitions.LoadWorkflow(new LoadWorkflowRequest { Bundle = built.Bundle });
            Output.Line($"{Output.Green("loaded")}     {Format.Reference(loaded.Reference)}");
            reference = loaded.Reference;
            return true;
        }

        if (!WorkflowRef.TryParse(value, out var parsed))
        {
            Output.ErrorLine(Output.Red(
                $"'{value}' is neither an existing file nor a valid workflow reference."));
            failure = ExitCodes.Usage;
            return false;
        }

        reference = new WorkflowReference { Name = parsed.Name };
        if (parsed.Namespace is not null) reference.Namespace = parsed.Namespace;
        if (parsed.Registry is not null) reference.Registry = parsed.Registry;
        if (parsed.Version is not null) reference.Version = parsed.Version;
        if (parsed.Digest is not null) reference.Digest = parsed.Digest;

        return true;
    }

    /// <summary>
    /// A reference never contains a path separator or a file extension, so those are what
    /// distinguish the two forms. Anything that exists on disk is a file regardless.
    /// </summary>
    private static bool LooksLikeFile(string value) =>
        File.Exists(value)
        || value.StartsWith("./", StringComparison.Ordinal)
        || value.StartsWith("../", StringComparison.Ordinal)
        || value.StartsWith(".\\", StringComparison.Ordinal)
        || value.StartsWith("..\\", StringComparison.Ordinal)
        || value.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);
}
