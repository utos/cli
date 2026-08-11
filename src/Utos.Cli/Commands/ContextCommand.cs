using System.CommandLine;
using Utos.Cli.Core.Daemon;

namespace Utos.Cli.Commands;

/// <summary>
/// <c>utos context</c> — manage the set of daemons this CLI knows about, and which one is current.
/// The Docker analogue is <c>docker context</c>.
/// </summary>
internal static class ContextCommand
{
    public static Command Create()
    {
        var command = new Command("context", "Manage configured daemons.")
        {
            Create_(),
            Use(),
            List(),
            Remove(),
        };

        return command;
    }

    private static Command Create_()
    {
        var name = new Argument<string>("name") { Description = "Name for the context." };
        var host = new Argument<string>("host") { Description = "Daemon address, e.g. http://localhost:5164." };

        var command = new Command("create", "Add a daemon and select it if none is selected.") { name, host };
        command.SetAction(parseResult =>
        {
            var config = ContextStore.Load();
            var contextName = parseResult.GetValue(name)!;

            if (ContextStore.Find(config, contextName) is { } existing)
            {
                existing.Host = parseResult.GetValue(host)!;
                Output.Line($"updated context {Output.Bold(existing.Name)} → {existing.Host}");
            }
            else
            {
                config.Contexts.Add(new DaemonContext { Name = contextName, Host = parseResult.GetValue(host)! });
                Output.Line($"created context {Output.Bold(contextName)} → {parseResult.GetValue(host)}");
            }

            // A first context with nothing selected is almost certainly the one you want.
            config.CurrentContext ??= contextName;

            ContextStore.Save(config);
            return ExitCodes.Success;
        });

        return command;
    }

    private static Command Use()
    {
        var name = new Argument<string>("name") { Description = "Context to make current." };

        var command = new Command("use", "Select the context commands use by default.") { name };
        command.SetAction(parseResult =>
        {
            var config = ContextStore.Load();
            var contextName = parseResult.GetValue(name)!;

            if (ContextStore.Find(config, contextName) is not { } found)
            {
                Output.ErrorLine(Output.Red($"No context named '{contextName}'."));
                return ExitCodes.Usage;
            }

            config.CurrentContext = found.Name;
            ContextStore.Save(config);
            Output.Line($"using {Output.Bold(found.Name)} → {found.Host}");
            return ExitCodes.Success;
        });

        return command;
    }

    private static Command List()
    {
        var command = new Command("ls", "List configured daemons.");
        command.SetAction(_ =>
        {
            var config = ContextStore.Load();
            if (config.Contexts.Count == 0)
            {
                Output.Line("No contexts configured. Add one with `utos context create <name> <host>`.");
                return ExitCodes.Success;
            }

            var width = config.Contexts.Max(c => c.Name.Length);
            foreach (var context in config.Contexts)
            {
                var current = string.Equals(context.Name, config.CurrentContext, StringComparison.OrdinalIgnoreCase);
                Output.Line($"{(current ? "*" : " ")} {context.Name.PadRight(width)}  {Output.Dim(context.Host)}");
            }

            return ExitCodes.Success;
        });

        return command;
    }

    private static Command Remove()
    {
        var name = new Argument<string>("name") { Description = "Context to remove." };

        var command = new Command("rm", "Remove a configured daemon.") { name };
        command.SetAction(parseResult =>
        {
            var config = ContextStore.Load();
            var contextName = parseResult.GetValue(name)!;

            if (ContextStore.Find(config, contextName) is not { } found)
            {
                Output.ErrorLine(Output.Red($"No context named '{contextName}'."));
                return ExitCodes.Usage;
            }

            config.Contexts.Remove(found);
            if (string.Equals(config.CurrentContext, found.Name, StringComparison.OrdinalIgnoreCase))
            {
                config.CurrentContext = config.Contexts.FirstOrDefault()?.Name;
            }

            ContextStore.Save(config);
            Output.Line($"removed context {Output.Bold(found.Name)}");
            return ExitCodes.Success;
        });

        return command;
    }
}
