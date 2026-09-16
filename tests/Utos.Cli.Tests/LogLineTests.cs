using Utos.Cli.Core.Rendering;
using Utos.Daemon.V1;
using Xunit;

namespace Utos.Cli.Tests;

public class LogLineTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 10, 9, 41, 7, TimeSpan.Zero);

    [Fact]
    public void Prints_time_level_and_message_and_nothing_else_for_a_system_event() =>
        Assert.Equal(
            "09:41:07  INFO   Activity 'assess' completed in 842ms",
            LogLine.Format(At, LogLevel.Info, "system", "Activity 'assess' completed in 842ms", colour: false));

    [Fact]
    public void Shows_a_source_other_than_system()
    {
        var line = LogLine.Format(At, LogLevel.Info, "settle-refund", "Activity 'refund' started", colour: false);

        Assert.Equal("09:41:07  INFO   settle-refund  Activity 'refund' started", line);
    }

    [Fact]
    public void Keeps_the_message_column_aligned_across_levels()
    {
        var info = LogLine.Format(At, LogLevel.Info, "system", "x", colour: false);
        var warn = LogLine.Format(At, LogLevel.Warn, "system", "x", colour: false);
        var error = LogLine.Format(At, LogLevel.Error, "system", "x", colour: false);

        Assert.Equal(info.IndexOf('x'), warn.IndexOf('x'));
        Assert.Equal(info.IndexOf('x'), error.IndexOf('x'));
    }

    [Fact]
    public void Styles_activity_names_durations_and_outcomes_when_colour_is_on()
    {
        var text = LogLine.Highlight("Activity 'assess' completed in 842ms", colour: true);

        Assert.Contains(Ansi.Bold + Ansi.Cyan + "'assess'" + Ansi.Reset, text);
        Assert.Contains(Ansi.Green + "completed" + Ansi.Reset, text);
        Assert.Contains(Ansi.Dim + "842ms" + Ansi.Reset, text);
    }

    [Fact]
    public void Bolds_a_workflow_reference_and_dims_the_execution_id_on_a_call_line()
    {
        var text = LogLine.Highlight(
            "Activity 'settle' calls acme/settle-refund:2.1.0 as execution 7c1e2f40-9a1b-4c3d-8e5f-0a1b2c3d4e5f",
            colour: true);

        Assert.Contains(Ansi.Bold + "acme/settle-refund:2.1.0" + Ansi.Reset, text);
        Assert.Contains(Ansi.Dim + "7c1e2f40-9a1b-4c3d-8e5f-0a1b2c3d4e5f" + Ansi.Reset, text);
    }

    [Fact]
    public void Colours_a_failure_red() =>
        Assert.Contains(Ansi.Red + "failed" + Ansi.Reset, LogLine.Highlight("Workflow execution failed: HTTP 500", colour: true));

    [Fact]
    public void Leaves_text_that_matches_no_pattern_exactly_as_sent()
    {
        // The daemon may reword its messages. Highlighting must never cost the reader anything.
        const string message = "Workflow scheduled starting from activity";

        Assert.Equal(message, LogLine.Highlight(message, colour: true));
    }

    [Fact]
    public void Writes_no_escape_codes_when_colour_is_off() =>
        // Ordinal: a culture-aware comparison ignores control characters, so it "finds" ESC in
        // any string at all and the assertion fails for the wrong reason.
        Assert.DoesNotContain(Ansi.Escape,
            LogLine.Format(At, LogLevel.Error, "system", "Activity 'x' failed in 3ms", colour: false),
            StringComparison.Ordinal);

    [Fact]
    public void Every_style_starts_with_a_real_escape_character()
    {
        // The tests above compare styled text against these same constants, so a constant that
        // lost its ESC would pass them all and print "[1m" to the terminal. This is the one test
        // that looks at the byte.
        foreach (var style in new[] { Ansi.Reset, Ansi.Bold, Ansi.Dim, Ansi.Red, Ansi.Green, Ansi.Yellow, Ansi.Cyan })
        {
            Assert.Equal(27, style[0]);
            Assert.Equal('[', style[1]);
        }
    }
}
