using System.Text.RegularExpressions;
using Utos.Daemon.V1;

namespace Utos.Cli.Core.Rendering;

/// <summary>
/// Renders one event from an execution's stream as a line of text.
/// <para>
/// The daemon's category is the .NET type that logged the event. It stays on the wire — it is what
/// <c>--category</c> filters on — but it is an implementation detail of one daemon, so it is not
/// printed. The source is printed only when it is something other than <c>system</c>, which is
/// every event today.
/// </para>
/// <para>
/// Highlighting works on the message text, which is not a contract: the daemon is free to reword
/// it. That is acceptable only because highlighting carries no meaning of its own — a message that
/// no pattern matches prints exactly as sent, and nothing a reader needs is lost.
/// </para>
/// </summary>
public static partial class LogLine
{
    private const string SystemSource = "system";

    public static string Format(DateTimeOffset? localTime, LogLevel level, string source, string message, bool colour)
    {
        var time = Ansi.Wrap(Ansi.Dim, localTime?.ToString("HH:mm:ss") ?? "--:--:--", colour);

        // Pad the plain label, then style it: padding a string that already carries escape codes
        // would count them as width and misalign the column.
        var label = (level switch
        {
            LogLevel.Unspecified => "",
            LogLevel.Warn => "WARN",
            _ => level.ToString().ToUpperInvariant(),
        }).PadRight(5);
        var levelText = level switch
        {
            LogLevel.Error or LogLevel.Fatal => Ansi.Wrap(Ansi.Red, label, colour),
            LogLevel.Warn => Ansi.Wrap(Ansi.Yellow, label, colour),
            _ => Ansi.Wrap(Ansi.Dim, label, colour),
        };

        var scope = string.IsNullOrEmpty(source) || source == SystemSource
            ? ""
            : Ansi.Wrap(Ansi.Dim, source, colour) + "  ";

        return $"{time}  {levelText}  {scope}{Highlight(message, colour)}";
    }

    /// <summary>
    /// Activity names in bold cyan, workflow references in bold, execution ids and durations
    /// dimmed, outcomes in their status colour. One pass over one alternation, so a match is never
    /// styled twice.
    /// </summary>
    public static string Highlight(string message, bool colour)
    {
        if (!colour) return message;

        return Highlights().Replace(message, m =>
            m.Groups["name"].Success ? Ansi.Bold + Ansi.Cyan + m.Value + Ansi.Reset
            : m.Groups["reference"].Success ? Ansi.Bold + m.Value + Ansi.Reset
            : m.Groups["ok"].Success ? Ansi.Green + m.Value + Ansi.Reset
            : m.Groups["bad"].Success ? Ansi.Red + m.Value + Ansi.Reset
            : Ansi.Dim + m.Value + Ansi.Reset);
    }

    // Source-generated rather than interpreted: NativeAOT, and no regex compilation at start-up.
    // A reference is `[registry/][namespace/]name:version`, and the version is what tells it apart
    // from any other word with a colon in it.
    [GeneratedRegex(
        @"(?<name>'[^'\s][^']*')"
        + @"|(?<reference>\b(?:[\w.-]+/)*[\w.-]+:\d+\.\d+\.\d+(?:[-+][\w.]+)?)"
        + @"|(?<id>\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b)"
        + @"|(?<ok>\b(?:completed|succeeded)\b)"
        + @"|(?<bad>\bfailed\b)"
        + @"|(?<dur>\b\d+(?:\.\d+)?(?:ms|s)\b)",
        RegexOptions.CultureInvariant)]
    private static partial Regex Highlights();
}
