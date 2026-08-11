using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Utos.Daemon.V1;

namespace Utos.Cli.Core.Daemon;

/// <summary>Turns command-line input and env arguments into their protobuf forms.</summary>
public static class PayloadParser
{
    /// <summary>
    /// Parses execution input. Accepts a JSON object literal, or <c>@path</c> to read one from a
    /// file — the same convention curl uses, and the reason a large input need not fit on a
    /// command line.
    /// </summary>
    /// <exception cref="ArgumentException">The input is not a JSON object.</exception>
    public static ExecutionPayload ParseInput(string? input)
    {
        var payload = new ExecutionPayload();
        if (string.IsNullOrWhiteSpace(input)) return payload;

        var json = input.StartsWith('@') ? ReadFile(input[1..]) : input;

        Struct parsed;
        try
        {
            parsed = JsonParser.Default.Parse<Struct>(json);
        }
        // Malformed JSON surfaces as InvalidJsonException, structural mismatches as
        // InvalidProtocolBufferException; both mean the same thing to whoever typed --input.
        catch (Exception ex) when (ex is InvalidProtocolBufferException or InvalidJsonException)
        {
            throw new ArgumentException($"--input is not valid JSON: {ex.Message}", ex);
        }

        foreach (var (key, value) in parsed.Fields) payload.Data[key] = value;
        return payload;
    }

    /// <summary>
    /// Parses repeated <c>KEY=VALUE</c> environment arguments, and <c>--env-file</c> lines in the
    /// same form. Values are strings by design: env originates as strings from shells, env files
    /// and CI variables, so the wire stays deliberately untyped.
    /// </summary>
    /// <exception cref="ArgumentException">An entry is not <c>KEY=VALUE</c>.</exception>
    public static Dictionary<string, string> ParseEnv(IEnumerable<string> entries, string? envFile)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(envFile))
        {
            foreach (var line in File.ReadAllLines(envFile))
            {
                var trimmed = line.Trim();
                // Blank lines and comments are conventional in env files.
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                Add(result, trimmed, $"{envFile}: ");
            }
        }

        foreach (var entry in entries) Add(result, entry, "--env ");
        return result;
    }

    private static void Add(Dictionary<string, string> result, string entry, string origin)
    {
        var separator = entry.IndexOf('=');
        if (separator <= 0)
        {
            throw new ArgumentException($"{origin}'{entry}' is not in KEY=VALUE form.");
        }

        result[entry[..separator]] = entry[(separator + 1)..];
    }

    private static string ReadFile(string path)
    {
        if (!File.Exists(path)) throw new ArgumentException($"--input file '{path}' does not exist.");
        return File.ReadAllText(path);
    }
}
