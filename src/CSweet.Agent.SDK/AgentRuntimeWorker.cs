using System.Text.Json;
using CSweet.Agent.Contracts.Packaging;
using CSweet.WorkManagement.Contracts;
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
    private readonly AgentDeliveryDispatcher _dispatcher = new(agent, logger);
    private volatile bool _configurationRestartRequested;

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
                await ApplyInitialConfigurationAsync(
                    agent,
                    session,
                    connectedContext,
                    stoppingToken);

                logger.LogInformation(
                    "Agent {AgentId} {Version} established configured MCP runtime session {SessionId} for installation {InstallationId}.",
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

                // Recovery is delivered through the platform's durable inbox. Startup has no
                // work lease and must not execute personal callbacks or inference.

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
                if (_configurationRestartRequested)
                    return;
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

    internal static async Task ApplyInitialConfigurationAsync(
        ICSweetAgent agent,
        AgentRuntimeSession session,
        AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        if (session.Configuration is null)
            return;
        if (agent is not CSweetAgentBase configurable)
            throw new InvalidOperationException("Platform configuration requires CSweetAgentBase.");
        try
        {
            await configurable.ApplyPlatformConfigurationAsync(session.Configuration, null, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"The platform installation configuration could not be applied: {exception.Message}", exception);
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

            AgentWorkLease? lease;
            try
            {
                lease = await runtime.ClaimAsync(maximumConcurrency - running.Count, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (running.Count > 0)
            {
                // Claim polling only fills unused capacity. A transient polling failure must not
                // tear down the session and cancel work that already owns a renewable lease.
                logger.LogWarning(exception,
                    "Agent work claim polling failed while {RunningCount} work item(s) remain active; preserving active leases and retrying.",
                    running.Count);
                await Task.Delay(RetryDelay, cancellationToken);
                continue;
            }
            if (lease is null)
                continue;
            if (lease.Kind == AgentWorkKind.ConfigurationUpdate)
            {
                if (running.Count > 0) await Task.WhenAll(running);
                running.Clear();
                await ProcessLeaseAsync(lease, platform, identity, cancellationToken);
                if (_configurationRestartRequested)
                    return;
                continue;
            }
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
        if (!ConfigureWorkDeadline(deadline, remaining))
        {
            await runtime.FailAsync(lease, "The work deadline elapsed before execution.", runtimeCancellation);
            return;
        }
        var progress = new LeaseProgressReporter(runtime, lease);
        using var inferenceScope = new InferenceExecutionScope(lease, deadline, progress);
        inferenceScope.Enter();
        var context = CreateContext(inferenceScope.BindPlatform(runtime), identity,
            new WorkScopedProgressReporter(progress, inferenceScope));
        context.Resources = lease.Resources;
        var renewal = RenewLeaseAsync(lease, deadline);
        try
        {
            var delivery = new AgentDelivery(lease.Kind, lease.WorkId, lease.Name, lease.Payload, lease.EventId, lease.CorrelationId);
            var result = await _dispatcher.DispatchAsync(delivery, context, deadline.Token);
            if (result.RestartRequired)
                _configurationRestartRequested = true;
            inferenceScope.Close();
            await runtime.CompleteAsync(lease, result.Work, runtimeCancellation);
        }
        catch (WorkloadResourceHandoffException handoff)
        {
            inferenceScope.Close();
            await runtime.CompleteAsync(lease, AgentWorkResult.ResourceHandoff(handoff.Request), runtimeCancellation);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !runtimeCancellation.IsCancellationRequested)
        {
            inferenceScope.Close();
            try
            {
                await runtime.FailAsync(lease, "The agent work deadline elapsed or was cancelled by the platform.", runtimeCancellation);
            }
            catch (Exception exception)
            {
                logger.LogInformation(exception,
                    "Work {WorkId} was already terminal when cancellation was acknowledged.", lease.WorkId);
            }
        }
        catch (Exception exception)
        {
            inferenceScope.Close();
            if (lease.Kind == AgentWorkKind.ConfigurationUpdate)
                _configurationRestartRequested = true;
            var diagnosticId = Guid.NewGuid();
            logger.LogError(exception,
                "Agent {AgentId} failed work {WorkId} ({WorkName}). Diagnostic {DiagnosticId}.",
                agent.AgentId, lease.WorkId, lease.Name, diagnosticId);
            // Keep a short header after the potentially long stack so bounded guest logs retain the cause.
            logger.LogError("Work failure summary {DiagnosticId}: {ExceptionType}: {Message}", diagnosticId,
                exception.GetType().Name, new string(exception.Message.Where(x => !char.IsControl(x)).Take(1400).ToArray()));
            await runtime.FailAsync(
                lease,
                DescribeFailure(exception, diagnosticId),
                runtimeCancellation);
        }
        finally
        {
            inferenceScope.Close();
            var cancellation = deadline.CancelAsync();
            try { await cancellation.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException)
            {
                _configurationRestartRequested = true;
                _ = cancellation.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                logger.LogWarning("Work cancellation cleanup exceeded its bound; the isolated runtime must stop.");
            }
            catch (Exception)
            {
                _configurationRestartRequested = true;
                logger.LogWarning("Work cancellation cleanup failed; the isolated runtime must stop.");
            }
            try { await renewal.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            catch (TimeoutException)
            {
                _configurationRestartRequested = true;
                _ = renewal.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                logger.LogWarning("Work lease renewal cleanup exceeded its bound; the isolated runtime must stop.");
            }
            catch (Exception)
            {
                _configurationRestartRequested = true;
                logger.LogWarning("Work lease renewal cleanup failed; the isolated runtime must stop.");
            }
        }
    }

    internal static string DescribeFailure(Exception exception, Guid diagnosticId)
    {
        if (exception is PlatformCapabilityException capability)
        {
            var code = !string.IsNullOrWhiteSpace(capability.FailureCode)
                ? SanitizeFailureToken(capability.FailureCode)
                : capability.Code switch
            {
                PlatformCapabilityErrorCode.Denied => "platform.capability.denied",
                PlatformCapabilityErrorCode.Unavailable => "platform.capability.unavailable",
                PlatformCapabilityErrorCode.NotFound => "platform.capability.not_found",
                PlatformCapabilityErrorCode.Conflict => "platform.capability.conflict",
                PlatformCapabilityErrorCode.ValidationFailed => "platform.capability.validation_failed",
                PlatformCapabilityErrorCode.ApprovalRequired => "platform.capability.approval_required",
                PlatformCapabilityErrorCode.BudgetExceeded => "platform.capability.budget_exceeded",
                _ => "platform.capability.unknown"
            };
            return $"agent-failure:v1;code={code};retryable={(capability.Retryable == true).ToString().ToLowerInvariant()};capability={SanitizeFailureToken(capability.Capability)};diagnosticId={diagnosticId:D}";
        }

        if (exception is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests })
            return $"agent-failure:v1;code=runtime.rate_limited;retryable=true;exceptionType={SanitizeFailureToken(exception.GetType().Name)};httpStatus=429;diagnosticId={diagnosticId:D}";
        if (exception is HttpRequestException transport)
            return $"agent-failure:v1;code=runtime.transport;retryable=true;exceptionType={SanitizeFailureToken(exception.GetType().Name)};{HttpStatusToken(transport)}diagnosticId={diagnosticId:D}";
        if (exception is JsonException)
            return $"agent-failure:v1;code=agent.payload_invalid;exceptionType={SanitizeFailureToken(exception.GetType().Name)};diagnosticId={diagnosticId:D}";
        if (exception is InvalidOperationException)
            return $"agent-failure:v1;code=agent.invalid_operation;exceptionType={SanitizeFailureToken(exception.GetType().Name)};diagnosticId={diagnosticId:D}";
        return $"agent-failure:v1;code=agent.unhandled;exceptionType={SanitizeFailureToken(exception.GetType().Name)};diagnosticId={diagnosticId:D}";
    }

    private static string HttpStatusToken(HttpRequestException exception) => exception.StatusCode is { } status
        ? $"httpStatus={(int)status};"
        : string.Empty;

    internal static bool ConfigureWorkDeadline(
        CancellationTokenSource cancellation,
        TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return false;

        // Platform work with no domain deadline is represented by DateTimeOffset.MaxValue.
        // CancelAfter rejects delays beyond the runtime timer range, while the enclosing
        // runtime cancellation still provides the installation's finite safety boundary.
        var maximumTimerDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);
        if (remaining <= maximumTimerDelay)
            cancellation.CancelAfter(remaining);
        return true;
    }

    private static string SanitizeFailureToken(string value) =>
        new(value.Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            .Take(300)
            .ToArray());

    private async Task RenewLeaseAsync(AgentWorkLease lease, CancellationTokenSource workCancellation)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(_options.LeaseRenewalSeconds, 5, 30));
        while (!workCancellation.IsCancellationRequested)
        {
            await Task.Delay(interval, workCancellation.Token);
            try
            {
                await runtime.RenewWorkAsync(lease, workCancellation.Token);
            }
            catch (OperationCanceledException) when (workCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Lease renewal stopped work {WorkId}; the platform may have cancelled or revoked it.",
                    lease.WorkId);
                await workCancellation.CancelAsync();
                return;
            }
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
        "Scheduled" => AgentActivationReason.Scheduled,
        "OnDemand" => AgentActivationReason.OnDemand,
        _ => AgentActivationReason.Unknown
    };

    private sealed class LeaseProgressReporter(
        IAgentRuntimeTransport runtime,
        AgentWorkLease lease) : IAgentProgressReporter
    {
        private long _sequence;

        private readonly SemaphoreSlim _gate = new(1, 1);
        public async Task ReportAsync(object? value, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                // Consume a number only once the platform accepts it. A failed report is resent with the
                // same number; the platform treats a number it already recorded as a duplicate. Before
                // 5.22.1 a failed report used up its number, so older platforms rejected every later
                // report as a gap (QFN Games, 2026-11-11).
                var next = _sequence + 1;
                await runtime.ReportProgressAsync(lease, next,
                    System.Text.Json.JsonSerializer.SerializeToElement(value), cancellationToken);
                _sequence = next;
            }
            finally { _gate.Release(); }
        }
    }

    private sealed class UnavailableProgressReporter : IAgentProgressReporter
    {
        public static readonly UnavailableProgressReporter Instance = new();

        public Task ReportAsync(object? value, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Progress can be reported only while processing leased work.");
    }
}
