using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>Stable lifecycle events emitted by the C-Sweet platform.</summary>
public static class AgentLifecycleEvents
{
    public const string Onboarded = "com.csweet.agent.onboarded.v1";
}

/// <summary>Payload delivered when an installed agent becomes an organization employee.</summary>
public sealed record AgentOnboardedEvent(
    Guid OrganizationId,
    Guid AgentOrganizationUserId,
    Guid HiringOrganizationUserId,
    Guid ConversationId,
    DateTimeOffset OccurredAt);

/// <summary>Request used internally by the typed lifecycle client.</summary>
public sealed record CompleteAgentOnboardingRequest(Guid EventId);

/// <summary>Result returned after the platform durably acknowledges agent onboarding.</summary>
public sealed record CompleteAgentOnboardingResponse(bool Completed, DateTimeOffset CompletedAt);

/// <summary>Typed lifecycle operations whose identifiers come from authoritative event envelopes.</summary>
public sealed class PlatformAgentLifecycleClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPlatformToolInvoker _tools;

    internal PlatformAgentLifecycleClient(IPlatformToolInvoker tools) => _tools = tools;

    /// <summary>
    /// Completes an onboarding event using its stable source-event identity, never its delivery work ID.
    /// </summary>
    public async Task<CompleteAgentOnboardingResponse> CompleteOnboardingAsync(
        AgentEventEnvelope message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!string.Equals(
                message.EventType,
                AgentLifecycleEvents.Onboarded,
                StringComparison.Ordinal))
            throw new ArgumentException(
                $"Event '{message.EventType}' is not an onboarding event.",
                nameof(message));
        if (message.EventId == Guid.Empty)
            throw new ArgumentException("The onboarding event ID is required.", nameof(message));

        var result = await _tools.InvokeAsync(
            AgentLifecycleCapabilities.CompleteOnboarding,
            JsonSerializer.SerializeToElement(
                new CompleteAgentOnboardingRequest(message.EventId),
                JsonOptions),
            cancellationToken);
        return result.Deserialize<CompleteAgentOnboardingResponse>(JsonOptions)
            ?? throw new PlatformCapabilityException(
                AgentLifecycleCapabilities.CompleteOnboarding,
                PlatformCapabilityErrorCode.ValidationFailed,
                "The platform returned an empty onboarding completion response.");
    }
}

/// <summary>Stable project-assignment events emitted by the C-Sweet platform.</summary>
public static class ProjectAssignmentEvents
{
    public const string Changed = "com.csweet.project-assignment.changed.v1";
    public const string Assigned = "com.csweet.project-assignment.assigned.v1";
    public const string Removed = "com.csweet.project-assignment.removed.v1";
}

/// <summary>
/// Payload delivered when the caller's project assignment changes. Agents must re-read
/// authoritative assignment on wake; the event is a wake hint, not a snapshot grant.
/// </summary>
public sealed record ProjectAssignmentChangedEvent(
    Guid OrganizationId,
    Guid WorkstreamId,
    Guid? TeamId,
    Guid? BoardId,
    string ChangeKind,
    long Revision,
    DateTimeOffset OccurredAt);
