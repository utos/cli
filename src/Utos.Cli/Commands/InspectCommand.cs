using System.CommandLine;
using Utos.Cli.Core.Build;
using Utos.Cli.Core.Source;
using Utos.Workflows.V1;

namespace Utos.Cli.Commands;

/// <summary>
/// <c>utos inspect</c> — show what a workflow resolved to: its dependency graph, the canonical
/// identities each alias bound to, and the resulting content digest.
/// <para>
/// This is the window onto the build stage. There is no <c>utos build</c> because a bundle is a
/// wire payload rather than a distributable artifact — what is worth seeing is the resolution, not
/// the bytes.
/// </para>
/// </summary>
internal static class InspectCommand
{
    public static Command Create()
    {
        var file = new Argument<string>("file")
        {
            Description = "Path to the workflow YAML file to inspect.",
        };

        var command = new Command("inspect", "Show a workflow's resolved dependency graph.")
        {
            file,
        };

        command.SetAction(parseResult => Run(parseResult.GetValue(file)!));
        return command;
    }

    private static int Run(string file)
    {
        BuildResult built;
        try
        {
            built = BundleBuilder.Build(file);
        }
        catch (WorkflowSourceException ex)
        {
            foreach (var issue in ex.Issues) Output.ErrorLine(Output.Red(issue.ToString()));
            return ExitCodes.ValidationFailed;
        }

        Output.Line(Output.Bold(built.Tree.Identity));
        WriteChildren(built.Tree, indent: string.Empty);

        Output.Line();
        Output.Line($"{built.Bundle.Workflows.Count} workflow(s)");

        // The digest format is still provisional upstream, so it is shown for inspection but never
        // sent as a guard on daemon calls.
        Output.Line($"digest  {Output.Dim(built.Bundle.ComputeContentDigest())}");
        return ExitCodes.Success;
    }

    private static void WriteChildren(DependencyNode node, string indent)
    {
        for (var i = 0; i < node.Dependencies.Count; i++)
        {
            var child = node.Dependencies[i];
            var last = i == node.Dependencies.Count - 1;

            Output.Line($"{indent}{(last ? "└── " : "├── ")}{Output.Dim(child.Alias + " → ")}{child.Identity}");
            WriteChildren(child, indent + (last ? "    " : "│   "));
        }
    }
}
