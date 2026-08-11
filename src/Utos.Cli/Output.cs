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
    private static readonly bool Colour =
        Environment.GetEnvironmentVariable("NO_COLOR") is null
        && Environment.GetEnvironmentVariable("TERM") != "dumb"
        && !Console.IsOutputRedirected;

    private const string Reset = "\u001b[0m";
    private const string RedCode = "\u001b[31m";
    private const string YellowCode = "\u001b[33m";
    private const string GreenCode = "\u001b[32m";
    private const string DimCode = "\u001b[2m";
    private const string BoldCode = "\u001b[1m";

    /// <summary>Writes a line to stdout.</summary>
    public static void Line(string text = "") => Console.Out.WriteLine(text);

    /// <summary>Writes a line to stderr.</summary>
    public static void ErrorLine(string text) => Console.Error.WriteLine(text);

    /// <summary>Red, for failures.</summary>
    public static string Red(string text) => Wrap(RedCode, text);

    /// <summary>Yellow, for warnings.</summary>
    public static string Yellow(string text) => Wrap(YellowCode, text);

    /// <summary>Green, for success.</summary>
    public static string Green(string text) => Wrap(GreenCode, text);

    /// <summary>Dimmed, for secondary detail.</summary>
    public static string Dim(string text) => Wrap(DimCode, text);

    /// <summary>Bold, for emphasis.</summary>
    public static string Bold(string text) => Wrap(BoldCode, text);

    private static string Wrap(string code, string text) => Colour ? code + text + Reset : text;
}
