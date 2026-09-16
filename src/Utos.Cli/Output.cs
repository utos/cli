using Utos.Cli.Core.Rendering;

namespace Utos.Cli;

/// <summary>Process exit codes. Distinct enough for a script to branch on.</summary>
public static class ExitCodes
{
    /// <summary>The command did what was asked.</summary>
    public const int Success = 0;

    /// <summary>Something went wrong that is not one of the cases below.</summary>
    public const int Error = 1;

    /// <summary>The command line itself was wrong.</summary>
    public const int Usage = 2;

    /// <summary>The workflow was read but is invalid.</summary>
    public const int ValidationFailed = 3;

    /// <summary>The daemon could not be reached, or refused the request.</summary>
    public const int DaemonError = 4;

    /// <summary>The workflow ran to completion but failed.</summary>
    public const int WorkflowFailed = 5;
}

/// <summary>
/// Console output with optional colour.
/// <para>
/// Deliberately hand-rolled rather than pulled from a rendering library: the output this CLI needs
/// is a few lines and a tree, and every dependency in a NativeAOT binary has to earn its trimming
/// risk. Honours <c>NO_COLOR</c> and detects redirection, so piping produces clean text.
/// </para>
/// </summary>
public static class Output
{
    /// <summary>
    /// Whether output is styled. <c>NO_COLOR</c> always wins. Otherwise colour is on for an
    /// interactive terminal, and can be forced with <c>FORCE_COLOR</c> or <c>CLICOLOR_FORCE</c> for
    /// the cases where redirected output is still headed for a screen — a CI log that renders ANSI,
    /// or a recording.
    /// </summary>
    public static bool Enabled { get; } =
        Environment.GetEnvironmentVariable("NO_COLOR") is null
        && (Forced("FORCE_COLOR") || Forced("CLICOLOR_FORCE")
            || (Environment.GetEnvironmentVariable("TERM") != "dumb" && !Console.IsOutputRedirected));

    /// <summary>Writes a line to stdout.</summary>
    public static void Line(string text = "") => Console.Out.WriteLine(text);

    /// <summary>Writes a line to stderr.</summary>
    public static void ErrorLine(string text) => Console.Error.WriteLine(text);

    /// <summary>Red, for failures.</summary>
    public static string Red(string text) => Ansi.Wrap(Ansi.Red, text, Enabled);

    /// <summary>Yellow, for warnings.</summary>
    public static string Yellow(string text) => Ansi.Wrap(Ansi.Yellow, text, Enabled);

    /// <summary>Green, for success.</summary>
    public static string Green(string text) => Ansi.Wrap(Ansi.Green, text, Enabled);

    /// <summary>Dimmed, for secondary detail.</summary>
    public static string Dim(string text) => Ansi.Wrap(Ansi.Dim, text, Enabled);

    /// <summary>Bold, for emphasis.</summary>
    public static string Bold(string text) => Ansi.Wrap(Ansi.Bold, text, Enabled);

    /// <summary>Set, and not the conventional "off" value.</summary>
    private static bool Forced(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value && value != "0";
}
