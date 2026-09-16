using System.CommandLine;
using Utos.Cli.Commands;

namespace Utos.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // UTF-8 regardless of the platform's console code page. On Windows, output that is piped or
        // redirected otherwise falls back to a legacy code page, which silently drops or
        // approximates anything outside it — the → in a transition line vanished, and inspect's tree
        // characters and an em dash in a workflow description degraded with it. No BOM: a BOM at
        // the start of piped output corrupts whatever reads it.
        Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var root = new RootCommand("Build, validate, load and run Utos workflows.")
        {
            ValidateCommand.Create(),
            InspectCommand.Create(),
            ContextCommand.Create(),
            VersionCommand.Create(),
            LoadCommand.Create(),
            RunCommand.Create(),
            LogsCommand.Create(),
            WorkflowCommand.Create(),
            ExecutionCommand.Create(),
            CancelCommand.Create(),
        };

        // Response files off. System.CommandLine otherwise expands any `@`-prefixed argument by
        // reading that file and splitting it into tokens before options are bound — which turned
        // `--input @ticket.json` into a usage error. `@` means "read this file" for --input, and
        // nothing in this CLI wants response files.
        var parsed = root.Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });

        // A parse error is a usage error, which gets its own exit code so a script can tell
        // "you typed it wrong" apart from "the workflow is invalid". Invoking still does the
        // reporting — System.CommandLine prints the errors and the relevant help — so this only
        // overrides the exit code.
        if (parsed.Errors.Count > 0)
        {
            await parsed.InvokeAsync();
            return ExitCodes.Usage;
        }

        try
        {
            return await parsed.InvokeAsync();
        }
        catch (Exception ex)
        {
            Output.ErrorLine(Output.Red(ex.Message));
            return ExitCodes.Error;
        }
    }
}
