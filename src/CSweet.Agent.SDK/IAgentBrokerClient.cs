using CSweet.Agent.Contracts.Grpc;

namespace CSweet.Agent.SDK;

public interface IAgentBrokerClient : IAsyncDisposable
{
    /// <summary>
    /// The accepted registration, including the capabilities granted to this runtime session.
    /// Custom broker clients may leave this unavailable; model-visible adapters should then
    /// default to exposing no broker tools.
    /// </summary>
    RegistrationResult? Registration => null;

    Task StartAsync(RegisterAgent registration, CancellationToken cancellationToken);

    IAsyncEnumerable<BrokerToAgentMessage> ReadAllAsync(CancellationToken cancellationToken);

    Task PublishEventAsync(
        PublishEvent message,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task<CapabilityResult> InvokeCapabilityAsync(
        RequestCapability request,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    async IAsyncEnumerable<CapabilityResult> InvokeStreamingCapabilityAsync(
        RequestCapability request,
        string? correlationId = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return await InvokeCapabilityAsync(request, correlationId, cancellationToken);
    }

    Task SendCapabilityResultAsync(
        CapabilityResult result,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken);
}
