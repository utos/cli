namespace Utos.Cli.Core.Rendering;

/// <summary>
/// The handful of ANSI styles the CLI uses. Kept in Core so text can be formatted — and tested —
/// without a terminal; whether colour is on is always the caller's decision, passed in.
/// </summary>
public static class Ansi
{
    /// <summary>
    /// The escape character, built from its code rather than written as an escape sequence. An
    /// escape sequence in source is one tool or editor away from being rewritten into a raw control
    /// character, or dropped — and a style constant without its ESC still compares equal to itself,
    /// so every test built on these constants would keep passing while the terminal showed
    /// <c>[1m</c> in the output.
    /// </summary>
    public static readonly string Escape = ((char)27).ToString();

    public static readonly string Reset = Escape + "[0m";
    public static readonly string Bold = Escape + "[1m";
    public static readonly string Dim = Escape + "[2m";
    public static readonly string Red = Escape + "[31m";
    public static readonly string Green = Escape + "[32m";
    public static readonly string Yellow = Escape + "[33m";
    public static readonly string Cyan = Escape + "[36m";

    /// <summary>Wraps <paramref name="text"/> in <paramref name="style"/> when colour is on.</summary>
    public static string Wrap(string style, string text, bool enabled) =>
        enabled && text.Length > 0 ? style + text + Reset : text;
}
