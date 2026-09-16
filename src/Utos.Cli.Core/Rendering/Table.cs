using System.Text;

namespace Utos.Cli.Core.Rendering;

/// <summary>
/// Aligns rows into columns for list commands.
/// <para>
/// Widths are measured on visible text, ignoring ANSI styling. Measuring the styled string would
/// count escape codes as width, and a coloured status column would push everything after it out of
/// line exactly when colour is on.
/// </para>
/// </summary>
public static class Table
{
    private const string Gap = "   ";

    public static IReadOnlyList<string> Format(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows, bool colour)
    {
        var body = rows.ToList();
        var widths = headers.Select(h => h.Length).ToArray();
        foreach (var row in body)
            for (var i = 0; i < widths.Length && i < row.Count; i++)
                widths[i] = Math.Max(widths[i], VisibleLength(row[i]));

        var lines = new List<string>(body.Count + 1)
        {
            Join(headers.Select(h => Ansi.Wrap(Ansi.Dim, h, colour)).ToList(), headers, widths),
        };
        lines.AddRange(body.Select(row => Join(row, row, widths)));
        return lines;
    }

    /// <summary>Length of <paramref name="text"/> as it appears on screen, without escape codes.</summary>
    public static int VisibleLength(string text)
    {
        var length = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == 27)
            {
                // Skip to the end of the sequence: ESC [ params m
                while (i < text.Length && text[i] != 'm') i++;
                continue;
            }
            length++;
        }
        return length;
    }

    private static string Join(IReadOnlyList<string> cells, IReadOnlyList<string> plain, int[] widths)
    {
        var line = new StringBuilder();
        for (var i = 0; i < cells.Count; i++)
        {
            line.Append(cells[i]);
            // No trailing padding after the last column.
            if (i < cells.Count - 1)
                line.Append(' ', widths[i] - VisibleLength(plain[i])).Append(Gap);
        }
        return line.ToString();
    }
}
