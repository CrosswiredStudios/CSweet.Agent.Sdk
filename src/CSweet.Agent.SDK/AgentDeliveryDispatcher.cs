using System.Text.Json;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.Logging;

namespace CSweet.Agent.SDK;

internal sealed record AgentDelivery(
    AgentWorkKind Kind, Guid WorkId, string Name, JsonElement Payload, Guid? EventId, string? CorrelationId);

internal sealed record AgentDispatchResult(AgentWorkResult Work, bool RestartRequired = false);

// One dispatcher per agent host. Concurrent wakes share the personal-work gate.
internal sealed class AgentDeliveryDispatcher(ICSweetAgent agent, ILogger logger)
{
    private readonly SemaphoreSlim _personalTodoGate = new(1, 1);

    internal async Task<AgentDispatchResult> DispatchAsync(
        AgentDelivery delivery, AgentRuntimeContext context, CancellationToken cancellationToken)
    {
        if (delivery.Kind == AgentWorkKind.ConfigurationUpdate)
            return await ApplyConfigurationUpdateAsync(delivery, cancellationToken);

        var result = delivery.Kind switch
        {
            AgentWorkKind.Capability => await agent.ExecuteCapabilityAsync(
                new AgentCapabilityRequest(delivery.WorkId, delivery.Name, delivery.Payload, delivery.CorrelationId),
                context, cancellationToken),
            AgentWorkKind.Event => await HandleEventAsync(delivery, context, cancellationToken),
            AgentWorkKind.Shutdown => AgentWorkResult.Success(new { acknowledged = true }),
            _ => AgentWorkResult.Failure($"Unsupported work kind '{delivery.Kind}'.")
        };
        return new(result);
    }

    private async Task<AgentDispatchResult> ApplyConfigurationUpdateAsync(
        AgentDelivery delivery,
        CancellationToken cancellationToken)
    {
        if (agent is not CSweetAgentBase configurable)
            return new(AgentWorkResult.Failure("Configuration refresh requires CSweetAgentBase."));
        var update = delivery.Payload.Deserialize<AgentConfigurationUpdate>(CSweetAgentBase.SerializerOptions)
            ?? throw new InvalidOperationException("The configuration refresh payload is empty.");
        var result = await configurable.ApplyPlatformConfigurationAsync(
            new AgentRuntimeConfiguration(update.SchemaVersion, update.EffectiveSettings, update.InstallationId,
                update.DesiredRevision, update.EffectiveDigest),
            update.ChangedKeys,
            cancellationToken);
        return new(AgentWorkResult.Success(new
        {
            appliedRevision = update.DesiredRevision,
            effectiveDigest = update.EffectiveDigest,
            result = result.ToString()
        }), result == ConfigurationApplyResult.RestartRequired);
    }

    private async Task<AgentWorkResult> HandleEventAsync(
        AgentDelivery delivery,
        AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        var envelope = new AgentEventEnvelope(
                delivery.WorkId,
                delivery.EventId is { } eventId && eventId != Guid.Empty
                    ? eventId
                    : throw new InvalidOperationException(
                        "The platform omitted the stable event ID for event work."),
                delivery.Name,
                delivery.Payload,
                DateTimeOffset.UtcNow,
                delivery.CorrelationId);
        if (agent is CSweetManagerAgentBase managerAgent && await ManagerIncidentCoordinator.DispatchAsync(managerAgent, envelope, context, cancellationToken))
            return AgentWorkResult.Success(new { acknowledged = true });
        if (agent is CSweetAgentBase delegationAgent && await DelegationRuntime.DispatchAsync(delegationAgent, envelope, context, cancellationToken))
            return AgentWorkResult.Success(new { acknowledged = true });
        if (string.Equals(envelope.EventType, CalendarEvents.ReminderDue, StringComparison.Ordinal) && agent is CSweetAgentBase calendarAgent)
        {
            var reminder = envelope.Data.Deserialize<CalendarReminderDueEvent>(CSweetAgentBase.SerializerOptions)
                ?? throw new InvalidOperationException("Calendar reminder payload is empty.");
            await calendarAgent.HandleCalendarReminderAsync(reminder, context, cancellationToken);
            return AgentWorkResult.Success(new { acknowledged = true });
        }
        if (string.Equals(envelope.EventType, AgentAttentionEvents.ReviewDue, StringComparison.Ordinal) &&
            agent is CSweetAgentBase attentiveAgent)
        {
            var review = envelope.Data.Deserialize<AgentAttentionReviewDueEvent>(
                CSweetAgentBase.SerializerOptions)
                ?? throw new InvalidOperationException("The attention review payload is empty.");
            await attentiveAgent.HandleAttentionReviewAsync(
                new AgentAttentionReviewContext(
                    review.ReviewId, review.OccurredAt, review.NextReviewAt, review.Reason)
                {
                    TriggerCategory = review.TriggerCategory,
                    CorrelationId = review.CorrelationId
                },
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
                PersonalTodoClaim claim;
                if (context.Resources is { ReplacementCount: > 0, ResumeWorkItemId: { } resumeItem })
                {
                    // This exact root came from the server-validated checkpoint handoff.
                    // Re-read current revision; never pick a different task on replacement.
                    var directory = await context.Platform.PersonalTodo.ListAsync(cancellationToken);
                    var retained = directory.Boards.SelectMany(x => x.Items).SingleOrDefault(x => x.Id == resumeItem && x.ArchivedAt is null);
                    claim = retained is null ? new PersonalTodoClaim(null) : await context.Platform.PersonalTodo.ClaimAsync(eventId, retained.Id, retained.Revision, cancellationToken);
                    if (claim.Item is null && retained is not null)
                    {
                        // Claim may have atomically expired the old VM's personal-work delivery.
                        // Refresh once and reclaim only that same root at its new revision.
                        directory = await context.Platform.PersonalTodo.ListAsync(cancellationToken);
                        retained = directory.Boards.SelectMany(x => x.Items).SingleOrDefault(x => x.Id == resumeItem && x.Status == PersonalTodoStatuses.Ready);
                        if (retained is not null) claim = await context.Platform.PersonalTodo.ClaimAsync(eventId, retained.Id, retained.Revision, cancellationToken);
                    }
                }
                else if (personalTodoAgent is IPersonalTodoClaimPolicy policy)
                {
                    var directory = await context.Platform.PersonalTodo.ListAsync(cancellationToken);
                    var ownerId = Guid.TryParse(context.Identity?.EmployeeId, out var parsedOwner) ? parsedOwner : directory.CurrentOrganizationUserId;
                    var candidates = directory.Boards
                        .Where(board => ownerId is null || board.OwnerOrganizationUserId == ownerId)
                        .SelectMany(board => board.Items)
                        .Where(item => item.ArchivedAt is null && item.IsExecutable &&
                            string.Equals(item.Status, PersonalTodoStatuses.Ready, StringComparison.Ordinal))
                        .OrderBy(item => item.Rank).ThenBy(item => item.CreatedAt)
                        .ToArray();
                    claim = new PersonalTodoClaim(null);
                    foreach (var candidate in candidates)
                    {
                        if (!WorkContributionLinks.IsCommitment(candidate) && !(personalTodoAgent is CSweetManagerAgentBase && (ManagerIncidentCoordinator.IsIncidentCommitment(candidate) || ManagerWorkRecovery.IsCommitment(candidate))) &&
                            await policy.EvaluatePersonalTodoClaimAsync(candidate, context, cancellationToken) != PersonalTodoClaimDecision.Claim)
                            continue;
                        claim = await context.Platform.PersonalTodo.ClaimAsync(
                            eventId, candidate.Id, candidate.Revision, cancellationToken);
                        break;
                    }
                }
                else
                {
                    claim = await context.Platform.PersonalTodo.ClaimAsync(eventId, cancellationToken);
                }
                if (claim.Item is null)
                    return;
                PersonalTodoResult result;
                try
                {
                    result = await WorkContributionLinks.HandleAsync(claim.Item, context, cancellationToken) ?? (personalTodoAgent is CSweetManagerAgentBase manager
                        ? await ManagerWorkRecovery.HandleAsync(manager, claim.Item, context, cancellationToken)
                            ?? await ManagerIncidentCoordinator.HandlePersonalCommitmentAsync(manager, claim.Item, context, cancellationToken)
                            ?? await personalTodoAgent.HandlePersonalTodoAsync(claim.Item, context, cancellationToken)
                        : await personalTodoAgent.HandlePersonalTodoAsync(claim.Item, context, cancellationToken));
                    if (result.RetryFailure is { } failure)
                        result = await PersonalTodoRetry.ResolveAsync(context.Platform, claim.Item, failure, cancellationToken);
                }
                catch (WorkloadResourceHandoffException) { throw; }
                catch (Exception error) when (error is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    // An unexpected failure is retried within a budget and keeps its real cause. Releasing and
                    // rethrowing left the item to be blocked later as "execution stopped unexpectedly".
                    PersonalTodoResult? retry = null;
                    try { retry = await PersonalTodoRetry.ResolveAsync(context.Platform, claim.Item, PersonalTodoRetry.Describe(error), cancellationToken); }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
                    if (retry is null)
                    {
                        await context.Platform.PersonalTodo.ReleaseAsync(
                            claim.Item.Id, eventId, context.Platform.PersonalTodo.ClaimedRevision(claim.Item.Id, claim.Item.Revision),
                            keepInProgress: false, cancellationToken);
                        throw;
                    }
                    logger.LogWarning(error, "Personal to-do {ItemId} failed and was scheduled for retry or blocked with its cause.", claim.Item.Id);
                    result = retry;
                }
                catch
                {
                    await context.Platform.PersonalTodo.ReleaseAsync(
                        claim.Item.Id, eventId, context.Platform.PersonalTodo.ClaimedRevision(claim.Item.Id, claim.Item.Revision),
                        keepInProgress: false, cancellationToken);
                    throw;
                }
                if (result.IsCompleted)
                {
                    await context.Platform.PersonalTodo.CompleteAsync(
                        claim.Item.Id, eventId, context.Platform.PersonalTodo.ClaimedRevision(claim.Item.Id, claim.Item.Revision),
                        string.IsNullOrEmpty(result.Content) ? null : result.Content,
                        cancellationToken);
                }
                else if (result.NextReviewAt is { } nextReviewAt)
                {
                    await context.Platform.PersonalTodo.DeferAsync(
                        claim.Item.Id, eventId, context.Platform.PersonalTodo.ClaimedRevision(claim.Item.Id, claim.Item.Revision),
                        nextReviewAt, result.Content, result.WaitingOnOrganizationUserId,
                        cancellationToken, result.Dependency);
                }
                else if (result.KeepInProgress)
                {
                    await context.Platform.PersonalTodo.ReleaseAsync(
                        claim.Item.Id, eventId, context.Platform.PersonalTodo.ClaimedRevision(claim.Item.Id, claim.Item.Revision),
                        keepInProgress: true, cancellationToken);
                }
                else
                {
                    await context.Platform.PersonalTodo.BlockAsync(
                        claim.Item.Id, eventId, context.Platform.PersonalTodo.ClaimedRevision(claim.Item.Id, claim.Item.Revision), result.Content,
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

}
