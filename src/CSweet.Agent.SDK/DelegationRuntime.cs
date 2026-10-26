using System.Text.Json;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK;

/// <summary>Source-bounded intent selected by the shared interaction classifier.</summary>
public sealed record DelegationIntent(string Kind, string? Recipient, string? Instruction, string? IntendedOutcome, string? Clarification);

internal static class DelegationRuntime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string ExecutionStatus(DelegationSnapshot d) => d.ExecutionStatus ?? d.Status;

    internal static async Task<bool> DispatchAsync(CSweetAgentBase agent, AgentEventEnvelope envelope,
        AgentRuntimeContext context, CancellationToken token)
    {
        if (envelope.EventType == DelegationCapabilities.Received)
        {
            var wake = envelope.Data.Deserialize<DelegationWake>(Json) ?? throw new JsonException();
            var d = await ReadCurrent(context, wake.DelegationId, token);
            if (d is null) return true;
            if (ExecutionStatus(d) != "AwaitingAcknowledgement") return true;
            var decision = await agent.HandleDelegationReceivedAsync(d, context, token);
            await Respond(context, d, decision, $"delegation-receive:{d.Id:N}", token);
            return true;
        }
        if (envelope.EventType == DelegationCapabilities.Changed)
        {
            var wake = envelope.Data.Deserialize<WorkTrackingWake>(Json) ?? throw new JsonException();
            if (wake.DelegationId is { } id)
            {
                var d = await ReadCurrent(context, id, token);
                if (d is not null && context.Identity?.EmployeeId == d.RecipientId.ToString("D") && ExecutionStatus(d) is "Active" or "Blocked")
                {
                    var outcome = await agent.ReviewDelegationOutcomeAsync(d, context, token);
                    if (outcome is not null) await Respond(context, d, outcome, $"delegation-outcome:{d.Id:N}:{d.Revision}", token);
                }
            }
            await ReportPending(agent, context, token);
            return true;
        }
        if (envelope.EventType == AgentAttentionEvents.ReviewDue)
        {
            if (!(await context.Platform.Tools.ListToolsAsync(token)).Any(x => x.Capability == DelegationCapabilities.Read)) return false;
            try
            {
                foreach (var candidate in await context.Platform.Delegation.ReadAsync(new(), token))
                {
                    var d = await ReadCurrent(context, candidate.Id, token);
                    if (d is null) continue;
                    if (d.RecipientId.ToString("D") == context.Identity?.EmployeeId && ExecutionStatus(d) == "AwaitingAcknowledgement")
                        await Respond(context, d, await agent.HandleDelegationReceivedAsync(d, context, token), $"delegation-receive:{d.Id:N}", token);
                    else if (d.RecipientId.ToString("D") == context.Identity?.EmployeeId && ExecutionStatus(d) is "Active" or "Blocked")
                    {
                        var outcome = await agent.ReviewDelegationOutcomeAsync(d, context, token);
                        if (outcome is not null) await Respond(context, d, outcome, $"delegation-outcome:{d.Id:N}:{d.Revision}", token);
                    }
                }
                await ReportPending(agent, context, token);
            }
            catch (PlatformCapabilityException e) when (e.Code is PlatformCapabilityErrorCode.Denied or PlatformCapabilityErrorCode.NotFound) { }
            return false;
        }
        if (envelope.EventType != CommunicationEvents.MessageReceived) return false;
        var incoming = envelope.Data.Deserialize<CommunicationMessageReceivedEvent>(Json);
        if (incoming is null || incoming.MessageId == Guid.Empty || !Guid.TryParse(incoming.ConversationId, out var chat)) return false;
        var tools = await context.Platform.Tools.ListToolsAsync(token);
        if (!tools.Any(x => x.Capability == DelegationCapabilities.Create)) return false;
        DelegationSnapshot? execution;
        try { execution = (await context.Platform.Delegation.ReadAsync(new(ExecutionMessageId: incoming.MessageId), token)).SingleOrDefault(); }
        catch (PlatformCapabilityException error) when (error.Code is PlatformCapabilityErrorCode.Denied or PlatformCapabilityErrorCode.NotFound) { return true; }
        if (execution is not null)
        {
            execution = await ReadCurrent(context, execution.Id, token);
            if (execution is null) return true;
            if (ExecutionStatus(execution) is "Cancelled" or "Completed" or "Declined" or "AttentionRequired") return true;
            if (execution.ExecutionHandled) return true;
            incoming = BindExecutionContext(execution, incoming, context);
            envelope = envelope with { Data = JsonSerializer.SerializeToElement(incoming, Json), WorkContext = incoming.WorkContext };
            var nested = await agent.ClassifyDelegationIntentAsync(incoming, context, token);
            if (nested.Kind != "Other")
            {
                var summary = await PublishIntent(nested, incoming, chat, context, token, execution.Id);
                await Respond(context, execution, new DelegationResponse.Progress("Execution response: " + summary), $"delegation-executed:{execution.Id:N}", token);
                return true;
            }
            var capture = new CapturedProgress(context);
            var child = new AgentRuntimeContext(context.BusinessId, context.InstallationId, context.RuntimeInstanceId,
                context.TickId, context.Platform, capture, context.Identity);
            await agent.HandleDelegationExecutionAsync(execution, incoming, envelope, child, token);
            if (capture.Final is null) await agent.HandleDelegationWorkIntakeAsync(execution, incoming, child, token);
            if (capture.Final is { Length: > 0 } response)
            {
                var work = await agent.ResolveDelegationWorkAsync(execution, response, context, token);
                if (work is { } item && item != execution.WorkItemId)
                    execution = await Respond(context, execution, new DelegationResponse.Acceptance(response, item), $"delegation-bind:{execution.Id:N}:{item:N}", token);
                await Respond(context, execution, new DelegationResponse.Progress("Execution response: " + response[..Math.Min(response.Length, 3900)]),
                    $"delegation-executed:{execution.Id:N}", token);
            }
            else await Respond(context, execution, new DelegationResponse.Blocker("The recipient has not provided an actionable execution response. Review the delegated request."),
                $"delegation-executed:{execution.Id:N}", token);
            return true;
        }
        var intent = await agent.ClassifyDelegationIntentAsync(incoming, context, token);
        if (intent.Kind == "Other") return false;
        await PublishIntent(intent, incoming, chat, context, token);
        return true;
    }

    internal static CommunicationMessageReceivedEvent BindExecutionContext(DelegationSnapshot execution,
        CommunicationMessageReceivedEvent incoming, AgentRuntimeContext context)
    {
        // Current authorized delegation state is authoritative; a chat transport may omit its project context.
        if (execution.WorkContext?.WorkstreamId is not { } project) return incoming;
        if (project == Guid.Empty || !Guid.TryParse(context.BusinessId, out var organization))
            throw new InvalidOperationException("Delegation execution requires a valid organization and project.");
        var source = execution.WorkContext;
        var prior = incoming.WorkContext;
        if (prior is not null && (prior.OrganizationId != organization || prior.WorkstreamId != project ||
            source.TeamId.HasValue && prior.TeamId.HasValue && source.TeamId != prior.TeamId ||
            source.WorkItemId.HasValue && prior.WorkItemId.HasValue && source.WorkItemId != prior.WorkItemId))
            throw new InvalidOperationException("Delegation execution context conflicts with the current authorized delegation.");
        var bound = prior ?? new AgentWorkContext(organization, project, source.TeamId, null,
            source.WorkItemId, null, null, execution.Id, null, null);
        return incoming with { WorkContext = bound with {
            TeamId = source.TeamId ?? bound.TeamId, WorkItemId = source.WorkItemId ?? bound.WorkItemId } };
    }

    private static async Task<string> PublishIntent(DelegationIntent intent, CommunicationMessageReceivedEvent incoming,
        Guid chat, AgentRuntimeContext context, CancellationToken token, Guid? parentId = null)
    {
        await using var stream = context.CreateTurnStream(incoming.ConversationId, incoming.TurnId, incoming.Attempt);
        if (intent.Kind == "Clarification" || string.IsNullOrWhiteSpace(intent.Recipient) || string.IsNullOrWhiteSpace(intent.Instruction) ||
            string.IsNullOrWhiteSpace(intent.IntendedOutcome))
        {
            var clarification = intent.Clarification ?? "Who should I ask, and what outcome should they work toward?";
            await stream.CommitAsync(clarification, token);
            return clarification;
        }
        var source = (await context.Platform.Communication.ReadChatAsync(chat, token)).Messages.Single(x => x.Id == incoming.MessageId);
        var directory = await context.Platform.ReadOrganizationSnapshotAsync(token);
        var mentions = source.Mentions ?? [];
        var people = directory.People.Where(x => x.IsActive && x.EmployeeType == "Agent").ToArray();
        var mentioned = people.Where(x => mentions.Any(m => m.OrganizationUserId == x.Id &&
            (m.DisplayText.TrimStart('@').Equals(intent.Recipient.TrimStart('@'), StringComparison.OrdinalIgnoreCase) ||
             m.DisplayName.Equals(intent.Recipient.TrimStart('@'), StringComparison.OrdinalIgnoreCase)))).ToArray();
        var candidates = mentioned.Length > 0 ? mentioned : people.Where(x =>
            x.DisplayName.Equals(intent.Recipient.TrimStart('@'), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1 || !source.Content.Contains(intent.Instruction, StringComparison.Ordinal))
        {
            var clarification = candidates.Length != 1 ? "Please identify the employee you want me to ask." : "Please specify the instruction you want me to pass on and the intended outcome.";
            await stream.CommitAsync(clarification, token);
            return clarification;
        }
        var recipient = candidates[0];
        await context.Platform.Delegation.DelegateWithTrackingAsync(new(recipient.Id, intent.Instruction, intent.IntendedOutcome,
            chat, source.Id, $"delegation-source:{source.Id:N}", WorkContext: incoming.WorkContext is { } wc
                ? new(wc.WorkstreamId, wc.TeamId, wc.WorkItemId) : null, ParentDelegationId: parentId), token);
        var accepted = $"OK, I’ll ask {recipient.DisplayName} and keep you updated.";
        await stream.CommitAsync(accepted, token);
        return accepted;
    }

    internal static async Task ReportPending(CSweetAgentBase agent, AgentRuntimeContext context, CancellationToken token)
    {
        var pending = await context.Platform.WorkTracking.PendingUpdatesAsync(new(20), token);
        foreach (var group in pending.Where(x => x.DelegationId is null).GroupBy(x => x.WorkItemId))
        {
            await agent.HandleWorkTrackingUpdateAsync(await context.Platform.WorkTracking.ReadAsync(new(group.Key), token), context, token);
            foreach (var update in group) await context.Platform.WorkTracking.MarkProcessedAsync(new(group.Key, update.Update.Id), token);
        }
        foreach (var update in pending.Where(x => x.DelegationId.HasValue).GroupBy(x => x.DelegationId))
        {
            var d = await ReadCurrent(context, update.Key!.Value, token);
            if (d is null || d.TrackingCancelled) continue;
            // Preserve urgent receipts; ordinary progress is represented by the newest due update.
            foreach (var item in update.Where(x => x.Update.Kind != "Progress").Concat(update.Where(x => x.Update.Kind == "Progress").TakeLast(1)))
            {
                var summary = await agent.SummarizeDelegationUpdateAsync(d, item.Update, context, token);
                d = await context.Platform.Delegation.PublishReportAsync(new(d.Id, item.Update.Id, summary, $"delegation-report:{item.Update.Id:N}"), token);
                if (d.Status is "Completed" or "Cancelled") break;
            }
        }
    }

    private static async Task<DelegationSnapshot?> ReadCurrent(AgentRuntimeContext context, Guid id, CancellationToken token)
    {
        try { return (await context.Platform.Delegation.ReadAsync(new(id), token)).SingleOrDefault(); }
        catch (PlatformCapabilityException error) when (error.Code is PlatformCapabilityErrorCode.Denied or PlatformCapabilityErrorCode.NotFound) { return null; }
    }

    private static async Task<DelegationSnapshot> Respond(AgentRuntimeContext context, DelegationSnapshot d, DelegationResponse response, string key, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            d = (await context.Platform.Delegation.ReadAsync(new(d.Id), token)).Single();
            try { return await context.Platform.Delegation.RespondAsync(new(d.Id, d.Revision, response switch
        {
            DelegationResponse.Acceptance => "Accepted", DelegationResponse.Clarification => "Clarification",
            DelegationResponse.Decline => "Declined", DelegationResponse.Blocker => "Blocked",
            DelegationResponse.FinalOutcome f => f.Succeeded ? "Completed" : "Failed", _ => "Progress"
        }, response.Summary, key, response is DelegationResponse.Acceptance a ? a.WorkItemId : null), token); }
            catch (PlatformCapabilityException e) when (e.Code == PlatformCapabilityErrorCode.Conflict && attempt < 2) { }
        }
    }

    private sealed class CapturedProgress(AgentRuntimeContext parent) : IAgentProgressReporter
    {
        public string? Final { get; private set; }
        public async Task ReportAsync(object? value, CancellationToken cancellationToken = default)
        {
            if (value is AgentTurnStreamEvent { Kind: AgentTurnStreamKinds.FinalCommit } final) Final = final.Delta;
            await parent.ReportProgressAsync(value, cancellationToken);
        }
    }
}
