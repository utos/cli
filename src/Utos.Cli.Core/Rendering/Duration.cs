namespace Utos.Cli.Core.Rendering;

/// <summary>Short, human durations for list output: <c>842ms</c>, <c>1.9s</c>, <c>3m 12s</c>, <c>2h 05m</c>.</summary>
public static class Duration
{
    public static string Format(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;

        return span.TotalMilliseconds < 1000 ? $"{(long)span.TotalMilliseconds}ms"
            : span.TotalSeconds < 60 ? $"{span.TotalSeconds:0.0}s"
            : span.TotalMinutes < 60 ? $"{(int)span.TotalMinutes}m {span.Seconds:00}s"
            : $"{(int)span.TotalHours}h {span.Minutes:00}m";
    }
}
