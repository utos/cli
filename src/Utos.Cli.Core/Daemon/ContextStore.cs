using System.Text.Json;
using System.Text.Json.Serialization;

namespace Utos.Cli.Core.Daemon;

/// <summary>A configured daemon endpoint.</summary>
public sealed class DaemonContext
{
    /// <summary>The name used to select this context.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The daemon's address, e.g. <c>http://localhost:5164</c>. The reference daemon speaks
    /// HTTP/2 cleartext, so an <c>http://</c> address is normal rather than a mistake.
    /// </summary>
    public string Host { get; set; } = string.Empty;
}

/// <summary>The CLI's on-disk configuration.</summary>
public sealed class CliConfig
{
    /// <summary>The context used when none is named explicitly.</summary>
    public string? CurrentContext { get; set; }

    /// <summary>Every configured context.</summary>
    public List<DaemonContext> Contexts { get; set; } = [];
}

// Source-generated so configuration serialization survives trimming in the published binary.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CliConfig))]
internal partial class ConfigJsonContext : JsonSerializerContext;

/// <summary>
/// Reads and writes <c>~/.utos/config.json</c>, and resolves which daemon a command should talk to.
/// </summary>
public static class ContextStore
{
    /// <summary>Environment variable that overrides the configured context.</summary>
    public const string HostVariable = "UTOS_HOST";

    /// <summary>Environment variable that relocates the configuration file.</summary>
    public const string ConfigVariable = "UTOS_CONFIG";

    /// <summary>
    /// The configuration file's path — <c>~/.utos/config.json</c>, or wherever
    /// <see cref="ConfigVariable"/> points. Computed per call rather than cached so the override
    /// takes effect for a process that sets it late, and so tests need not touch a real profile.
    /// </summary>
    public static string ConfigPath
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(ConfigVariable);
            if (!string.IsNullOrWhiteSpace(overridden)) return overridden;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".utos", "config.json");
        }
    }

    /// <summary>Loads the configuration, returning an empty one when the file does not exist.</summary>
    public static CliConfig Load()
    {
        if (!File.Exists(ConfigPath)) return new CliConfig();

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(ConfigPath), ConfigJsonContext.Default.CliConfig)
                   ?? new CliConfig();
        }
        catch (JsonException ex)
        {
            throw new DaemonConfigurationException($"{ConfigPath} is not valid JSON: {ex.Message}");
        }
    }

    /// <summary>Writes the configuration, creating the directory if needed.</summary>
    public static void Save(CliConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, ConfigJsonContext.Default.CliConfig));
    }

    /// <summary>
    /// Resolves the daemon address, in precedence order: an explicit <c>--host</c>, then a named
    /// <c>--context</c>, then <c>UTOS_HOST</c>, then the current context.
    /// </summary>
    /// <exception cref="DaemonConfigurationException">No daemon can be determined.</exception>
    public static string ResolveHost(string? host, string? contextName)
    {
        if (!string.IsNullOrWhiteSpace(host)) return host;

        var config = Load();

        if (!string.IsNullOrWhiteSpace(contextName))
        {
            var named = Find(config, contextName)
                        ?? throw new DaemonConfigurationException(
                            $"No context named '{contextName}'. Configured: {Names(config)}.");
            return named.Host;
        }

        var variable = Environment.GetEnvironmentVariable(HostVariable);
        if (!string.IsNullOrWhiteSpace(variable)) return variable;

        if (string.IsNullOrWhiteSpace(config.CurrentContext))
        {
            throw new DaemonConfigurationException(
                "No daemon configured. Run `utos context create <name> <host>`, pass --host, "
                + $"or set {HostVariable}.");
        }

        var current = Find(config, config.CurrentContext)
                      ?? throw new DaemonConfigurationException(
                          $"Current context '{config.CurrentContext}' no longer exists. "
                          + $"Configured: {Names(config)}.");

        return current.Host;
    }

    /// <summary>Finds a context by name, ordinally and case-insensitively.</summary>
    public static DaemonContext? Find(CliConfig config, string name) =>
        config.Contexts.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string Names(CliConfig config) =>
        config.Contexts.Count == 0 ? "(none)" : string.Join(", ", config.Contexts.Select(c => c.Name));
}

/// <summary>Thrown when the CLI cannot work out which daemon to talk to.</summary>
public sealed class DaemonConfigurationException(string message) : Exception(message);
