using CSweet.Agent.Contracts.Packaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CSweet.Agent.SDK;

internal sealed class AgentRuntimeWorker<TAgent>(
    TAgent agent,
    IAgentRuntimeTransport runtime,
    AgentPlatformAccessor platformAccessor,
    IOptions<AgentRuntimeOptions> options,
    ILogger<AgentRuntimeWorker<TAgent>> logger) : BackgroundService
    where TAgent : class, ICSweetAgent
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private readonly AgentRuntimeOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var manifest = await AgentManifestLoader.LoadAsync(_options.ManifestPath, stoppingToken);
        ValidateIdentity(manifest);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var session = await runtime.InitializeAsync(manifest, stoppingToken);
                var platform = new PlatformCapabilityClient(runtime);
                platformAccessor.SetCurrent(platform);
                var connectedContext = CreateContext(platform, session.Identity, UnavailableProgressReporter.Instance);

                logger.LogInformation(
                    "Agent {AgentId} {Version} established MCP runtime session {SessionId} for installation {InstallationId}.",
                    agent.AgentId,
                    agent.Version,
                    session.SessionId,
                    _options.InstallationId);

                if (agent is IAgentActivationHandler activation)
                {
                    await activation.OnActivatedAsync(
                        new AgentActivationContext(
                            ResolveActivationReason(manifest.Runtime.DefaultActivationMode),
                            _options.RuntimeInstanceId,
                            _options.TickId,
                            DateTimeOffset.UtcNow),
                        connectedContext,
                        stoppingToken);
                }

                using var connected = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var workTask = RunWorkLoopAsync(
                    manifest,
                    platform,
                    session.Identity,
                    connected.Token);
                var serviceTask = agent is IAgentConnectedService service
                    ? service.RunConnectedAsync(connectedContext, connected.Token)
                    : Task.Delay(Timeout.InfiniteTimeSpan, connected.Token);
                var completed = await Task.WhenAny(workTask, serviceTask);
                if (completed == serviceTask && serviceTask.IsCompletedSuccessfully)
                    await runtime.CompleteRuntimeAsync(AgentWorkResult.Success(new { }), stoppingToken);
                await connected.CancelAsync();
                try { await Task.WhenAll(workTask, serviceTask); }
                catch (OperationCanceledException) when (connected.IsCancellationRequested) { }
                if (completed.IsFaulted)
                    await completed;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Agent {AgentId} MCP runtime failed. Reconnecting in {RetrySeconds} seconds.",
                    agent.AgentId,
                    RetryDelay.TotalSeconds);
            }

            await Task.Delay(RetryDelay, stoppingToken);
        }
    }

    private async Task RunWorkLoopAsync(
        AgentManifest manifest,
        PlatformCapabilityClient platform,
        AgentIdentity? identity,
        CancellationToken cancellationToken)
    {
        var maximumConcurrency = Math.Max(1, manifest.Runtime.MaximumConcurrentJobs);
        var running = new HashSet<Task>();
        while (!cancellationToken.IsCancellationRequested)
        {
            running.RemoveWhere(task => task.IsCompleted);
            if (running.Count >= maximumConcurrency)
            {
                await Task.WhenAny(running);
                continue;
            }

            var lease = await runtime.ClaimAsync(maximumConcurrency - running.Count, cancellationToken);
            if (lease is null)
                continue;
            var task = ProcessLeaseAsync(lease, platform, identity, cancellationToken);
            running.Add(task);
            _ = task.ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                        logger.LogError(completed.Exception, "Agent work {WorkId} failed outside the lease handler.", lease.WorkId);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        await Task.WhenAll(running);
    }

    private async Task ProcessLeaseAsync(
        AgentWorkLease lease,
        PlatformCapabilityClient platform,
        AgentIdentity? identity,
        CancellationToken runtimeCancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(runtimeCancellation);
        var remaining = lease.Deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            await runtime.FailAsync(lease, "The work deadline elapsed before execution.", runtimeCancellation);
            return;
        }
        deadline.CancelAfter(remaining);
        var progress = new LeaseProgressReporter(runtime, lease);
        var context = CreateContext(platform, identity, progress);
        var renewal = RenewLeaseAsync(lease, deadline.Token);
        try
        {
            AgentWorkResult result = lease.Kind switch
            {
                AgentWorkKind.Capability => await agent.ExecuteCapabilityAsync(
                    new AgentCapabilityRequest(
                        lease.WorkId,
                        lease.Name,
                        lease.Payload,
                        lease.CorrelationId),
                    context,
                    deadline.Token),
                AgentWorkKind.Event => await HandleEventAsync(lease, context, deadline.Token),
                AgentWorkKind.Shutdown => AgentWorkResult.Success(new { acknowledged = true }),
                _ => AgentWorkResult.Failure($"Unsupported work kind '{lease.Kind}'.")
            };
            await runtime.CompleteAsync(lease, result, runtimeCancellation);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !runtimeCancellation.IsCancellationRequested)
        {
            await runtime.FailAsync(lease, "The agent work deadline elapsed.", runtimeCancellation);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Agent {AgentId} failed work {WorkId} ({WorkName}).", agent.AgentId, lease.WorkId, lease.Name);
            await runtime.FailAsync(lease, "The agent failed while processing the work item.", runtimeCancellation);
        }
        finally
        {
            await deadline.CancelAsync();
            try { await renewal; }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
    }

    private async Task<AgentWorkResult> HandleEventAsync(
        AgentWorkLease lease,
        AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        await agent.HandleEventAsync(
            new AgentEventEnvelope(
                lease.WorkId,
                lease.EventId is { } eventId && eventId != Guid.Empty
                    ? eventId
                    : throw new InvalidOperationException(
                        "The platform omitted the stable event ID for event work."),
                lease.Name,
                lease.Payload,
                DateTimeOffset.UtcNow,
                lease.CorrelationId),
            context,
            cancellationToken);
        return AgentWorkResult.Success(new { acknowledged = true });
    }

    private async Task RenewLeaseAsync(AgentWorkLease lease, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(_options.LeaseRenewalSeconds, 5, 30));
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(interval, cancellationToken);
            await runtime.RenewWorkAsync(lease, cancellationToken);
        }
    }

    private AgentRuntimeContext CreateContext(
        PlatformCapabilityClient platform,
        AgentIdentity? identity,
        IAgentProgressReporter progress) =>
        new(
            _options.BusinessId,
            _options.InstallationId,
            _options.RuntimeInstanceId,
            _options.TickId,
            platform,
            progress,
            identity);

    private void ValidateIdentity(AgentManifest manifest)
    {
        if (!string.Equals(manifest.Id, agent.AgentId, StringComparison.Ordinal) ||
            !string.Equals(manifest.Version, agent.Version, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The agent implementation identity/version does not match csweet-plugin.json.");
        if (!Version.TryParse(manifest.Protocol.MinimumVersion, out var minimum) || minimum.Major < 2)
            throw new InvalidOperationException("Executable agents must declare protocol minimumVersion 2.0 or newer.");
    }

    private static AgentActivationReason ResolveActivationReason(string? mode) => mode switch
    {
        "AlwaysOn" => AgentActivationReason.AlwaysOnStartup,
        "Periodic" => AgentActivationReason.Scheduled,
        "Manual" => AgentActivationReason.Manual,
        _ => AgentActivationReason.Unknown
    };

    private sealed class LeaseProgressReporter(
        IAgentRuntimeTransport runtime,
        AgentWorkLease lease) : IAgentProgressReporter
    {
        private long _sequence;

        public Task ReportAsync(object? value, CancellationToken cancellationToken = default) =>
            runtime.ReportProgressAsync(
                lease,
                Interlocked.Increment(ref _sequence),
                System.Text.Json.JsonSerializer.SerializeToElement(value),
                cancellationToken);
    }

    private sealed class UnavailableProgressReporter : IAgentProgressReporter
    {
        public static readonly UnavailableProgressReporter Instance = new();

        public Task ReportAsync(object? value, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Progress can be reported only while processing leased work.");
    }
}
