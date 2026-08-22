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
    private readonly SemaphoreSlim _personalTodoGate = new(1, 1);
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

                if (agent is CSweetAgentBase personalTodoAgent &&
                    ShouldRecoverPersonalTodoOnStartup(manifest))
                {
                    try
                    {
                        await DrainPersonalTodoAsync(
                            Guid.NewGuid(), personalTodoAgent, connectedContext, stoppingToken);
                    }
                    catch (PlatformCapabilityException exception)
                    {
                        logger.LogWarning(exception,
                            "Agent {AgentId} could not perform its personal queue startup sweep; subscribed wake events will still be processed.",
                            agent.AgentId);
                    }
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

    internal static bool ShouldRecoverPersonalTodoOnStartup(AgentManifest manifest) =>
        manifest.Events.Subscribes.Contains(PersonalTodoEvents.Available, StringComparer.Ordinal) &&
        manifest.Requires.Any(x =>
            string.Equals(x.Name, PersonalTodoCapabilities.Claim, StringComparison.Ordinal));

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

            var lease = await runtime.ClaimAsync(maximumConcurrency - running.Count, cancellationToken);
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
        if (remaining <= TimeSpan.Zero)
        {
            await runtime.FailAsync(lease, "The work deadline elapsed before execution.", runtimeCancellation);
            return;
        }
        deadline.CancelAfter(remaining);
        var progress = new LeaseProgressReporter(runtime, lease);
        var context = CreateContext(platform, identity, progress);
        var renewal = RenewLeaseAsync(lease, deadline);
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
                AgentWorkKind.ConfigurationUpdate => await ApplyConfigurationUpdateAsync(lease, deadline.Token),
                AgentWorkKind.Shutdown => AgentWorkResult.Success(new { acknowledged = true }),
                _ => AgentWorkResult.Failure($"Unsupported work kind '{lease.Kind}'.")
            };
            await runtime.CompleteAsync(lease, result, runtimeCancellation);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !runtimeCancellation.IsCancellationRequested)
        {
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
            if (lease.Kind == AgentWorkKind.ConfigurationUpdate)
                _configurationRestartRequested = true;
            var diagnosticId = Guid.NewGuid();
            logger.LogError(exception,
                "Agent {AgentId} failed work {WorkId} ({WorkName}). Diagnostic {DiagnosticId}.",
                agent.AgentId, lease.WorkId, lease.Name, diagnosticId);
            await runtime.FailAsync(
                lease,
                DescribeFailure(exception, diagnosticId),
                runtimeCancellation);
        }
        finally
        {
            await deadline.CancelAsync();
            try { await renewal; }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
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

        if (exception is HttpRequestException)
            return $"agent-failure:v1;code=runtime.transport;diagnosticId={diagnosticId:D}";
        if (exception is JsonException)
            return $"agent-failure:v1;code=agent.payload_invalid;diagnosticId={diagnosticId:D}";
        if (exception is InvalidOperationException)
            return $"agent-failure:v1;code=agent.invalid_operation;diagnosticId={diagnosticId:D}";
        return $"agent-failure:v1;code=agent.unhandled;diagnosticId={diagnosticId:D}";
    }

    private static string SanitizeFailureToken(string value) =>
        new(value.Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            .Take(300)
            .ToArray());

    private async Task<AgentWorkResult> ApplyConfigurationUpdateAsync(
        AgentWorkLease lease,
        CancellationToken cancellationToken)
    {
        if (agent is not CSweetAgentBase configurable)
            return AgentWorkResult.Failure("Configuration refresh requires CSweetAgentBase.");
        var update = lease.Payload.Deserialize<AgentConfigurationUpdate>(CSweetAgentBase.SerializerOptions)
            ?? throw new InvalidOperationException("The configuration refresh payload is empty.");
        var result = await configurable.ApplyPlatformConfigurationAsync(
            new AgentRuntimeConfiguration(update.SchemaVersion, update.EffectiveSettings, update.InstallationId,
                update.DesiredRevision, update.EffectiveDigest),
            update.ChangedKeys,
            cancellationToken);
        if (result == ConfigurationApplyResult.RestartRequired)
            _configurationRestartRequested = true;
        return AgentWorkResult.Success(new
        {
            appliedRevision = update.DesiredRevision,
            effectiveDigest = update.EffectiveDigest,
            result = result.ToString()
        });
    }

    private async Task<AgentWorkResult> HandleEventAsync(
        AgentWorkLease lease,
        AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        var envelope = new AgentEventEnvelope(
                lease.WorkId,
                lease.EventId is { } eventId && eventId != Guid.Empty
                    ? eventId
                    : throw new InvalidOperationException(
                        "The platform omitted the stable event ID for event work."),
                lease.Name,
                lease.Payload,
                DateTimeOffset.UtcNow,
                lease.CorrelationId);
        if (string.Equals(envelope.EventType, AgentAttentionEvents.ReviewDue, StringComparison.Ordinal) &&
            agent is CSweetAgentBase attentiveAgent)
        {
            var review = envelope.Data.Deserialize<AgentAttentionReviewDueEvent>(
                CSweetAgentBase.SerializerOptions)
                ?? throw new InvalidOperationException("The attention review payload is empty.");
            await attentiveAgent.HandleAttentionReviewAsync(
                new AgentAttentionReviewContext(
                    review.ReviewId, review.OccurredAt, review.NextReviewAt, review.Reason),
                context, cancellationToken);
            try
            {
                await DrainPersonalTodoAsync(
                    envelope.EventId, attentiveAgent, context, cancellationToken, maximumItems: 3);
            }
            catch (PlatformCapabilityException exception) when (
                string.Equals(exception.Capability, PersonalTodoCapabilities.Claim, StringComparison.Ordinal) &&
                exception.Code is PlatformCapabilityErrorCode.Denied or PlatformCapabilityErrorCode.NotFound)
            {
                logger.LogDebug(exception,
                    "Agent {AgentId} does not have an actionable personal queue for this attention review.",
                    agent.AgentId);
            }
        }
        else if (string.Equals(envelope.EventType, AgentCoordinationEvents.TurnRequested, StringComparison.Ordinal) &&
            agent is CSweetAgentBase baseAgent)
        {
            var request = envelope.Data.Deserialize<AgentCoordinationTurnRequest>(
                CSweetAgentBase.SerializerOptions)
                ?? throw new InvalidOperationException("The coordination turn payload is empty.");
            var result = await baseAgent.HandleCoordinationTurnAsync(request, context, cancellationToken);
            ValidateCoordinationResult(request, result);
            await context.Platform.Communication.RespondToCoordinationAsync(
                new RespondToAgentCoordinationRequest(
                    request.SessionId,
                    request.ExpectedRevision,
                    request.TurnOrdinal,
                    result.Disposition,
                    result.Content,
                    $"coordination-turn:{request.SessionId:N}:{request.TurnOrdinal}",
                    result.Artifact),
                cancellationToken);
        }
        else if (string.Equals(envelope.EventType, PersonalTodoEvents.Available,
                     StringComparison.Ordinal) && agent is CSweetAgentBase personalTodoAgent)
        {
            await DrainPersonalTodoAsync(
                envelope.EventId, personalTodoAgent, context, cancellationToken, maximumItems: 3);
        }
        else
        {
            await agent.HandleEventAsync(envelope, context, cancellationToken);
        }
        return AgentWorkResult.Success(new { acknowledged = true });
    }

    private async Task DrainPersonalTodoAsync(
        Guid eventId,
        CSweetAgentBase personalTodoAgent,
        AgentRuntimeContext context,
        CancellationToken cancellationToken,
        int maximumItems = 3)
    {
        await _personalTodoGate.WaitAsync(cancellationToken);
        try
        {
            for (var handled = 0; handled < maximumItems; handled++)
            {
                var claim = await context.Platform.PersonalTodo.ClaimAsync(eventId, cancellationToken);
                if (claim.Item is null)
                    return;
                PersonalTodoResult result;
                try
                {
                    result = await personalTodoAgent.HandlePersonalTodoAsync(
                        claim.Item, context, cancellationToken);
                }
                catch
                {
                    await context.Platform.PersonalTodo.ReleaseAsync(
                        claim.Item.Id, eventId, claim.Item.Revision,
                        keepInProgress: false, cancellationToken);
                    throw;
                }
                if (result.IsCompleted)
                {
                    await context.Platform.PersonalTodo.CompleteAsync(
                        claim.Item.Id, eventId, claim.Item.Revision,
                        string.IsNullOrEmpty(result.Content) ? null : result.Content,
                        cancellationToken);
                }
                else if (result.NextReviewAt is { } nextReviewAt)
                {
                    await context.Platform.PersonalTodo.DeferAsync(
                        claim.Item.Id, eventId, claim.Item.Revision,
                        nextReviewAt, result.Content, result.WaitingOnOrganizationUserId,
                        cancellationToken);
                }
                else if (result.KeepInProgress)
                {
                    await context.Platform.PersonalTodo.ReleaseAsync(
                        claim.Item.Id, eventId, claim.Item.Revision,
                        keepInProgress: true, cancellationToken);
                }
                else
                {
                    await context.Platform.PersonalTodo.BlockAsync(
                        claim.Item.Id, eventId, claim.Item.Revision, result.Content,
                        cancellationToken);
                }
            }
        }
        finally
        {
            _personalTodoGate.Release();
        }
    }

    private static void ValidateCoordinationResult(
        AgentCoordinationTurnRequest request,
        AgentCoordinationTurnResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Content))
            throw new InvalidOperationException("A coordination turn must return substantive content.");
        if (result.Disposition == AgentCoordinationDispositions.Continue &&
            IsAcknowledgementOnly(result.Content))
            throw new InvalidOperationException(
                "A Continue coordination turn must include substantive progress and a concrete question or next request.");
        if (result.Disposition is not (AgentCoordinationDispositions.Continue or
            AgentCoordinationDispositions.Completed or AgentCoordinationDispositions.Blocked))
            throw new InvalidOperationException("A coordination turn must Continue, Complete, or Block.");
        if (request.IsFinalization && result.Disposition == AgentCoordinationDispositions.Continue)
            throw new InvalidOperationException("A finalization turn cannot continue the collaboration.");
        if (result.Artifact is { } artifact)
            ValidateCoordinationArtifact(artifact);
    }

    private static void ValidateCoordinationArtifact(AgentCoordinationArtifactSubmission artifact)
    {
        const int maximumArtifactBytes = 256 * 1024;
        if (string.IsNullOrWhiteSpace(artifact.Type) || artifact.Type.Length > 200)
            throw new InvalidOperationException("A coordination artifact requires a stable type of at most 200 characters.");
        if (string.IsNullOrWhiteSpace(artifact.SchemaVersion) || artifact.SchemaVersion.Length > 50)
            throw new InvalidOperationException("A coordination artifact requires a schema version of at most 50 characters.");
        if (string.IsNullOrWhiteSpace(artifact.Key) || artifact.Key.Length > 500)
            throw new InvalidOperationException("A coordination artifact requires a stable key of at most 500 characters.");
        if (artifact.PageOrdinal < 0)
            throw new InvalidOperationException("A coordination artifact page ordinal cannot be negative.");
        if (artifact.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new InvalidOperationException("A coordination artifact requires a JSON payload.");
        if (System.Text.Encoding.UTF8.GetByteCount(artifact.Payload.GetRawText()) > maximumArtifactBytes)
            throw new InvalidOperationException("A coordination artifact cannot exceed 256 KiB.");
    }

    private static bool IsAcknowledgementOnly(string content)
    {
        var normalized = content.Trim().TrimEnd('.', '!', '?').ToLowerInvariant();
        return normalized is "ok" or "okay" or "ack" or "acknowledged" or "understood" or
            "thanks" or "thank you" or "sounds good" or "will do";
    }

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
