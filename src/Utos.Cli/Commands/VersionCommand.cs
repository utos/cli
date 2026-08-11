using System.CommandLine;
using System.Reflection;
using Utos.Daemon.V1;

namespace Utos.Cli.Commands;

/// <summary>
/// <c>utos version</c> — the CLI's own version, and the daemon's if one can be reached. The
/// quickest way to confirm a context actually points at something.
/// </summary>
internal static class VersionCommand
{
    public static Command Create()
    {
        var options = new DaemonOptions();
        var command = new Command("version", "Show CLI and daemon versions.");
        options.AddTo(command);

        command.SetAction((parseResult, _) => DaemonOptions.Guard(async () =>
        {
            Output.Line($"{Output.Bold("utos")}    {CliVersion()}");

            using var daemon = options.Connect(parseResult);
            var health = await daemon.Observability.GetHealthAsync(new GetHealthRequest());

            var status = health.Status switch
            {
                HealthStatus.Healthy => Output.Green("healthy"),
                HealthStatus.Unhealthy => Output.Red("unhealthy"),
                _ => Output.Yellow("unknown"),
            };

            Output.Line($"{Output.Bold("daemon")}  {health.Version}  {status}  {Output.Dim(daemon.Host)}");
            return ExitCodes.Success;
        }));

        return command;
    }

    private static string CliVersion() =>
        typeof(VersionCommand).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? "unknown";
}
