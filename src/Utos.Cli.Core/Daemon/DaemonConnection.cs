using Grpc.Core;
using Grpc.Net.Client;
using Utos.Daemon.V1;

namespace Utos.Cli.Core.Daemon;

/// <summary>
/// A connection to a daemon, exposing the three services it offers.
/// <para>
/// The reference daemon is HTTP/2 cleartext with no TLS and no authentication, so nothing here
/// carries credentials. The seam exists — <see cref="CallInvoker"/> is what tests substitute, and
/// is where interceptors would attach — but there is no server-side hook to authenticate against
/// yet.
/// </para>
/// </summary>
public sealed class DaemonConnection : IDisposable
{
    private readonly GrpcChannel? _channel;

    private DaemonConnection(CallInvoker invoker, string host, GrpcChannel? channel)
    {
        _channel = channel;
        Host = host;
        Definitions = new DefinitionService.DefinitionServiceClient(invoker);
        Executions = new ExecutionService.ExecutionServiceClient(invoker);
        Observability = new ObservabilityService.ObservabilityServiceClient(invoker);
    }

    /// <summary>Connects to <paramref name="host"/>.</summary>
    public static DaemonConnection Connect(string host)
    {
        if (!Uri.TryCreate(host, UriKind.Absolute, out var address)
            || (address.Scheme != "http" && address.Scheme != "https"))
        {
            throw new DaemonConfigurationException(
                $"'{host}' is not a valid daemon address; expected something like http://localhost:5164.");
        }

        var channel = GrpcChannel.ForAddress(address);
        return new DaemonConnection(channel.CreateCallInvoker(), host, channel);
    }

    /// <summary>Builds a connection over an arbitrary invoker, so tests need no server.</summary>
    public static DaemonConnection ForInvoker(CallInvoker invoker, string host = "test") =>
        new(invoker, host, channel: null);

    /// <summary>The address this connection was opened against.</summary>
    public string Host { get; }

    /// <summary>Load, list, get and unload workflow definitions.</summary>
    public DefinitionService.DefinitionServiceClient Definitions { get; }

    /// <summary>Schedule, get, list and delete executions.</summary>
    public ExecutionService.ExecutionServiceClient Executions { get; }

    /// <summary>Watch execution events, and check health.</summary>
    public ObservabilityService.ObservabilityServiceClient Observability { get; }

    /// <inheritdoc/>
    public void Dispose() => _channel?.Dispose();
}
