using System.CommandLine;
using Utos.Cli.Commands;

namespace Utos.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var root = new RootCommand("Build, validate, load and run Utos workflows.")
        {
            ValidateCommand.Create(),
            InspectCommand.Create(),
            ContextCommand.Create(),
            VersionCommand.Create(),
            LoadCommand.Create(),
            RunCommand.Create(),
            LogsCommand.Create(),
        };

        var parsed = root.Parse(args);

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
