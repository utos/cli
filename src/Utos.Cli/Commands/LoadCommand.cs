using System.CommandLine;
using Utos.Cli.Core.Build;
using Utos.Cli.Core.Source;
using Utos.Daemon.V1;
using Utos.Workflows.V1.Validation;

namespace Utos.Cli.Commands;

/// <summary>
/// <c>utos load</c> — resolve and validate a workflow, then put the resulting bundle in the
/// daemon's definition store. The daemon derives the definition's identity from the entry-point
/// workflow's metadata, so nothing here asserts a name.
/// </summary>
internal static class LoadCommand
{
    public static Command Create()
    {
        var file = new Argument<string>("file") { Description = "Path to the workflow YAML file to load." };
        var options = new DaemonOptions();

        var command = new Command("load", "Resolve, validate and load a workflow onto a daemon.") { file };
        options.AddTo(command);

        command.SetAction((parseResult, _) => DaemonOptions.Guard(async () =>
        {
            if (!Pipeline.TryResolve(parseResult.GetValue(file)!, out var built, out var failure)) return failure;

            using var daemon = options.Connect(parseResult);
            var response = await daemon.Definitions.LoadWorkflowAsync(
                new LoadWorkflowRequest { Bundle = built.Bundle });

            Output.Line($"{Output.Green("loaded")}  {Format.Reference(response.Reference)}");
            return ExitCodes.Success;
        }));

        return command;
    }
}

/// <summary>Shared resolve-and-validate step for the commands that need a bundle.</summary>
internal static class Pipeline
{
    /// <summary>
    /// Resolves and validates <paramref name="file"/>. Returns false with an exit code when the
    /// workflow cannot be used, having already reported why.
    /// </summary>
    public static bool TryResolve(string file, out BuildResult built, out int failure)
    {
        built = null!;
        failure = ExitCodes.ValidationFailed;

        try
        {
            built = BundleBuilder.Build(file);
        }
        catch (WorkflowSourceException ex)
        {
            foreach (var issue in ex.Issues) Output.ErrorLine(Output.Red(issue.ToString()));
            return false;
        }

        var report = WorkflowBundleValidator.Validate(built.Bundle);
        if (report.IsValid) return true;

        // Validating before sending means a bad workflow fails the same way whether or not a
        // daemon is reachable, and with a better message than a flattened InvalidArgument.
        foreach (var issue in report.Issues)
        {
            Output.ErrorLine($"{Output.Red(issue.Code)} {Output.Dim(issue.Path)}");
            Output.ErrorLine($"  {issue.Message}");
        }

        return false;
    }
}

/// <summary>Rendering helpers for daemon types.</summary>
internal static class Format
{
    /// <summary>Renders a reference in its canonical <c>[registry/][namespace/]name:version</c> form.</summary>
    public static string Reference(WorkflowReference reference)
    {
        var text = reference.Name;
        if (reference.HasNamespace) text = reference.Namespace + "/" + text;
        if (reference.HasRegistry) text = reference.Registry + "/" + text;
        if (reference.HasVersion) text += ":" + reference.Version;

        return text;
    }
}
