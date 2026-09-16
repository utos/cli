using System.CommandLine;
using Utos.Cli.Core.Rendering;
using Utos.Daemon.V1;
using Utos.Workflows.V1;

namespace Utos.Cli.Commands;

/// <summary>
/// <c>utos workflow ls|rm</c> — the workflows loaded on a daemon. Loading stays <c>utos load</c>, the
/// verb people already use; this group is for seeing and removing what is there.
/// </summary>
internal static class WorkflowCommand
{
    public static Command Create() =>
        new("workflow", "List and remove workflows loaded on a daemon.")
        {
            List(),
            Remove(),
        };

    private static Command List()
    {
        var options = new DaemonOptions();
        var command = new Command("ls", "List loaded workflows.");
        options.AddTo(command);

        command.SetAction((parseResult, cancellationToken) => DaemonOptions.Guard(async () =>
        {
            using var daemon = options.Connect(parseResult);
            var response = await daemon.Definitions.ListWorkflowsAsync(
                new ListWorkflowsRequest(), cancellationToken: cancellationToken);

            if (response.Workflows.Count == 0)
            {
                Output.Line("No workflows loaded. Load one with `utos load <file>`.");
                return ExitCodes.Success;
            }

            var rows = response.Workflows
                .OrderBy(w => Format.Reference(w.Reference), StringComparer.Ordinal)
                .Select(w => (IReadOnlyList<string>)
                [
                    Output.Bold(Format.Reference(w.Reference)),
                    Output.Dim(ShortDigest(w.Reference)),
                    Output.Dim(w.LoadedAt?.ToDateTimeOffset().ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? ""),
                    w.HasDescription ? w.Description : "",
                ]);

            foreach (var line in Table.Format(["WORKFLOW", "DIGEST", "LOADED", "DESCRIPTION"], rows, Output.Enabled))
                Output.Line(line);

            return ExitCodes.Success;
        }));

        return command;
    }

    private static Command Remove()
    {
        var reference = new Argument<string>("reference")
        {
            Description = "The workflow to remove, e.g. acme/hello:1.0.0. Without a version, every loaded version is removed.",
        };

        var options = new DaemonOptions();
        var command = new Command("rm", "Remove a loaded workflow.") { reference };
        options.AddTo(command);

        command.SetAction((parseResult, cancellationToken) => DaemonOptions.Guard(async () =>
        {
            var value = parseResult.GetValue(reference)!;
            if (!WorkflowRef.TryParse(value, out var parsed))
            {
                Output.ErrorLine(Output.Red($"'{value}' is not a valid workflow reference."));
                return ExitCodes.Usage;
            }

            var request = new UnloadWorkflowRequest { Reference = new WorkflowReference { Name = parsed.Name } };
            if (parsed.Namespace is not null) request.Reference.Namespace = parsed.Namespace;
            if (parsed.Registry is not null) request.Reference.Registry = parsed.Registry;
            if (parsed.Version is not null) request.Reference.Version = parsed.Version;
            if (parsed.Digest is not null) request.Reference.Digest = parsed.Digest;

            using var daemon = options.Connect(parseResult);
            var response = await daemon.Definitions.UnloadWorkflowAsync(request, cancellationToken: cancellationToken);

            // Zero is not an error on the wire, but it is almost always a typo, and saying
            // "removed" for nothing would hide it.
            if (response.Removed == 0)
            {
                Output.ErrorLine(Output.Red($"No loaded workflow matches '{value}'."));
                return ExitCodes.Error;
            }

            var versions = response.Removed == 1 ? "" : Output.Dim($"  ({response.Removed} versions)");
            Output.Line($"{Output.Green("removed")}  {value}{versions}");
            return ExitCodes.Success;
        }));

        return command;
    }

    /// <summary><c>sha256:</c> and the first 12 hex characters — enough to tell two builds apart.</summary>
    private static string ShortDigest(WorkflowReference reference)
    {
        if (!reference.HasDigest) return "";
        var digest = reference.Digest;
        var colon = digest.IndexOf(':');
        return colon >= 0 && digest.Length > colon + 13 ? digest[..(colon + 13)] : digest;
    }
}
