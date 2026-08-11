using System.CommandLine;
using System.Text.Json;
using Utos.Cli.Core.Build;
using Utos.Cli.Core.Source;
using Utos.Workflows.V1.Validation;

namespace Utos.Cli.Commands;

/// <summary>
/// <c>utos validate</c> — resolve a workflow and its dependencies, then check the resulting bundle
/// against the spec rules.
/// <para>
/// Resolution happens here because the interesting rules are all post-resolution: an entry point
/// must be a key of the bundle, transition targets must resolve, aliases must bind. None of that
/// is answerable from a single unresolved file.
/// </para>
/// </summary>
internal static class ValidateCommand
{
    public static Command Create()
    {
        var file = new Argument<string>("file")
        {
            Description = "Path to the workflow YAML file to validate.",
        };

        var json = new Option<bool>("--json")
        {
            Description = "Emit machine-readable JSON instead of human-readable text.",
        };

        var command = new Command("validate", "Validate a workflow and its dependencies.")
        {
            file,
            json,
        };

        command.SetAction(parseResult =>
            Run(parseResult.GetValue(file)!, parseResult.GetValue(json)));

        return command;
    }

    private static int Run(string file, bool asJson)
    {
        BuildResult built;
        try
        {
            built = BundleBuilder.Build(file);
        }
        catch (WorkflowSourceException ex)
        {
            return Report(asJson, ex.Issues.Select(i => new Row(i.Code, Location(i), i.Message)).ToList(), file);
        }

        var report = WorkflowBundleValidator.Validate(built.Bundle);
        var rows = report.Issues.Select(i => new Row(i.Code, i.Path, i.Message)).ToList();

        return Report(asJson, rows, file);
    }

    private static string Location(SourceIssue issue) =>
        issue.Line > 0 ? $"{issue.File}:{issue.Line}" : issue.File;

    private static int Report(bool asJson, IReadOnlyList<Row> rows, string file)
    {
        if (asJson) return ReportJson(rows);

        if (rows.Count == 0)
        {
            Output.Line($"{Output.Green("valid")}  {file}");
            return ExitCodes.Success;
        }

        foreach (var row in rows)
        {
            Output.ErrorLine($"{Output.Red(row.Code)} {Output.Dim(row.Path)}");
            Output.ErrorLine($"  {row.Message}");
        }

        Output.ErrorLine(string.Empty);
        Output.ErrorLine(Output.Red($"{rows.Count} problem{(rows.Count == 1 ? "" : "s")}"));
        return ExitCodes.ValidationFailed;
    }

    private static int ReportJson(IReadOnlyList<Row> rows)
    {
        // Written with Utf8JsonWriter rather than a serializer: no reflection, so nothing here
        // depends on trimming behaviour in the published binary.
        using var stream = Console.OpenStandardOutput();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("valid", rows.Count == 0);
            writer.WriteStartArray("issues");

            foreach (var row in rows)
            {
                writer.WriteStartObject();
                writer.WriteString("code", row.Code);
                writer.WriteString("path", row.Path);
                writer.WriteString("message", row.Message);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        Console.Out.WriteLine();
        return rows.Count == 0 ? ExitCodes.Success : ExitCodes.ValidationFailed;
    }

    private readonly record struct Row(string Code, string Path, string Message);
}
