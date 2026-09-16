using Utos.Cli.Core.Rendering;
using Xunit;

namespace Utos.Cli.Tests;

public class TableTests
{
    [Fact]
    public void Aligns_columns_to_the_widest_cell()
    {
        var lines = Table.Format(
            ["WORKFLOW", "STATUS"],
            [["acme/refund-request:1.0.0", "completed"], ["hello:1.0.0", "failed"]],
            colour: false);

        Assert.Equal(
            [
                "WORKFLOW                    STATUS",
                "acme/refund-request:1.0.0   completed",
                "hello:1.0.0                 failed",
            ],
            lines);
    }

    [Fact]
    public void Ignores_escape_codes_when_measuring_width()
    {
        // A coloured cell is longer as a string than on screen. Measuring the string would push the
        // next column right exactly when colour is on.
        var green = Ansi.Green + "completed" + Ansi.Reset;

        var lines = Table.Format(
            ["STATUS", "DURATION"],
            [[green, "1.9s"], ["failed", "842ms"]],
            colour: false);

        Assert.Equal(lines[1].IndexOf("1.9s", StringComparison.Ordinal) - (green.Length - "completed".Length),
            lines[2].IndexOf("842ms", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(842, "842ms")]
    [InlineData(1_925, "1.9s")]
    [InlineData(192_000, "3m 12s")]
    [InlineData(7_500_000, "2h 05m")]
    public void Formats_durations_at_a_readable_precision(long milliseconds, string expected) =>
        Assert.Equal(expected, Duration.Format(TimeSpan.FromMilliseconds(milliseconds)));
}
