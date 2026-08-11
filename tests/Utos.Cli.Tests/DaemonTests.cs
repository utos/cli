using Grpc.Core;
using Utos.Cli.Core.Daemon;
using Utos.Daemon.V1;
using Xunit;

namespace Utos.Cli.Tests;

public class PayloadParserTests
{
    [Fact]
    public void Parses_a_json_object_into_execution_input()
    {
        var payload = PayloadParser.ParseInput("""{"orderId": 42, "express": true, "tags": ["a","b"]}""");

        Assert.Equal(42, payload.Data["orderId"].NumberValue);
        Assert.True(payload.Data["express"].BoolValue);
        Assert.Equal(2, payload.Data["tags"].ListValue.Values.Count);
    }

    [Fact]
    public void Treats_an_empty_input_as_no_data() =>
        Assert.Empty(PayloadParser.ParseInput(null).Data);

    [Fact]
    public void Reads_input_from_a_file_with_the_at_prefix()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"fromFile": true}""");
            Assert.True(PayloadParser.ParseInput("@" + path).Data["fromFile"].BoolValue);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Rejects_input_that_is_not_valid_json() =>
        Assert.Throws<ArgumentException>(() => PayloadParser.ParseInput("{not json"));

    [Fact]
    public void Parses_repeated_key_value_env_arguments()
    {
        var env = PayloadParser.ParseEnv(["API_BASE=https://api.dev", "RETRIES=3"], envFile: null);

        Assert.Equal("https://api.dev", env["API_BASE"]);
        Assert.Equal("3", env["RETRIES"]);
    }

    [Fact]
    public void Keeps_everything_after_the_first_equals()
    {
        // A connection string or URL with '=' in it must survive intact.
        var env = PayloadParser.ParseEnv(["QUERY=a=1&b=2"], envFile: null);

        Assert.Equal("a=1&b=2", env["QUERY"]);
    }

    [Fact]
    public void Rejects_an_entry_that_is_not_key_value() =>
        Assert.Throws<ArgumentException>(() => PayloadParser.ParseEnv(["JUST_A_NAME"], envFile: null));

    [Fact]
    public void Reads_an_env_file_and_lets_arguments_win()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "# a comment\n\nAPI_BASE=https://from-file\nOTHER=kept\n");

            var env = PayloadParser.ParseEnv(["API_BASE=https://from-argument"], path);

            Assert.Equal("https://from-argument", env["API_BASE"]);
            Assert.Equal("kept", env["OTHER"]);
            Assert.Equal(2, env.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ContextStoreTests : IDisposable
{
    private readonly string _config = Path.Combine(
        Path.GetTempPath(), "utos-cli-" + Guid.NewGuid().ToString("N"), "config.json");

    public ContextStoreTests() =>
        Environment.SetEnvironmentVariable(ContextStore.ConfigVariable, _config);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ContextStore.ConfigVariable, null);
        Environment.SetEnvironmentVariable(ContextStore.HostVariable, null);

        var directory = Path.GetDirectoryName(_config)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void Round_trips_contexts()
    {
        ContextStore.Save(new CliConfig
        {
            CurrentContext = "local",
            Contexts = [new DaemonContext { Name = "local", Host = "http://localhost:5164" }],
        });

        var loaded = ContextStore.Load();

        Assert.Equal("local", loaded.CurrentContext);
        Assert.Equal("http://localhost:5164", Assert.Single(loaded.Contexts).Host);
    }

    [Fact]
    public void Returns_an_empty_configuration_when_no_file_exists() =>
        Assert.Empty(ContextStore.Load().Contexts);

    [Fact]
    public void Resolves_in_precedence_order()
    {
        ContextStore.Save(new CliConfig
        {
            CurrentContext = "current",
            Contexts =
            [
                new DaemonContext { Name = "current", Host = "http://current:5164" },
                new DaemonContext { Name = "other", Host = "http://other:5164" },
            ],
        });
        Environment.SetEnvironmentVariable(ContextStore.HostVariable, "http://from-env:5164");

        // --host beats everything; --context beats the environment; the environment beats current.
        Assert.Equal("http://explicit:5164", ContextStore.ResolveHost("http://explicit:5164", "other"));
        Assert.Equal("http://other:5164", ContextStore.ResolveHost(null, "other"));
        Assert.Equal("http://from-env:5164", ContextStore.ResolveHost(null, null));

        Environment.SetEnvironmentVariable(ContextStore.HostVariable, null);
        Assert.Equal("http://current:5164", ContextStore.ResolveHost(null, null));
    }

    [Fact]
    public void Explains_itself_when_nothing_is_configured()
    {
        var error = Assert.Throws<DaemonConfigurationException>(() => ContextStore.ResolveHost(null, null));

        Assert.Contains("utos context create", error.Message);
    }

    [Fact]
    public void Reports_an_unknown_context_by_name()
    {
        ContextStore.Save(new CliConfig
        {
            Contexts = [new DaemonContext { Name = "local", Host = "http://localhost:5164" }],
        });

        var error = Assert.Throws<DaemonConfigurationException>(() => ContextStore.ResolveHost(null, "nope"));

        Assert.Contains("local", error.Message);
    }
}

public class DaemonConnectionTests
{
    [Fact]
    public async Task Talks_to_the_daemon_through_a_substituted_invoker()
    {
        // The invoker seam is what makes daemon-facing code testable without a server — and
        // without referencing Utos.Daemon.Server, which cannot coexist with the client.
        var connection = DaemonConnection.ForInvoker(
            new StubInvoker(new GetHealthResponse { Status = HealthStatus.Healthy, Version = "9.9.9" }));

        var health = await connection.Observability.GetHealthAsync(new GetHealthRequest());

        Assert.Equal(HealthStatus.Healthy, health.Status);
        Assert.Equal("9.9.9", health.Version);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://localhost:5164")]
    public void Rejects_an_address_that_is_not_http(string host) =>
        Assert.Throws<DaemonConfigurationException>(() => DaemonConnection.Connect(host));

    /// <summary>A <see cref="CallInvoker"/> that answers every unary call with a canned response.</summary>
    private sealed class StubInvoker(object response) : CallInvoker
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            new(Task.FromResult((TResponse)response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => [],
                () => { });

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            (TResponse)response;

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) =>
            throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options) =>
            throw new NotSupportedException();
    }
}
