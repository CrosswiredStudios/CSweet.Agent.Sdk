namespace CSweet.Agent.SDK;

/// <summary>Common manager behavior. The manifest must independently request its capabilities and subscriptions.</summary>
public abstract class CSweetManagerAgentBase : CSweetAgentBase
{
    public string BaseType => AgentBaseTypes.Manager;
    protected abstract string ManagementResponsibility { get; }

    // Sealed entrypoints keep monitoring independent of the specialized workflow's intake state.
    public sealed override async Task HandleEventAsync(AgentEventEnvelope message, AgentRuntimeContext context, CancellationToken token)
    {
        if (ProjectIncidentReview.Handles(message.EventType))
            await ProjectIncidentReview.HandleManagerAsync(message, context, AssessIncidentAsync, token);
        else
            await HandleManagerEventAsync(message, context, token);
    }

    public sealed override async Task HandleAttentionReviewAsync(AgentAttentionReviewContext review, AgentRuntimeContext context, CancellationToken token)
    {
        await ProjectIncidentReview.RecoverManagerAsync(context, AssessIncidentAsync, token);
        await HandleManagerAttentionReviewAsync(review, context, token);
    }

    /// <summary>Override for role-specific assessment or existing authorized recovery. Use the supplied stable action key.
    /// Returning AwaitingRecovery or Investigating records a bounded follow-up; it neither extends escalation nor resolves the incident.</summary>
    protected virtual Task<ManagerIncidentAssessment> AssessIncidentAsync(ManagerIncidentContext incident, AgentRuntimeContext context, CancellationToken token) =>
        Task.FromResult(ManagerIncidentAssessment.Escalate(
            $"My responsibility is {ManagementResponsibility}. Available evidence does not establish an authorized resolution within my remit; manager review is needed."));

    protected virtual Task HandleManagerEventAsync(AgentEventEnvelope message, AgentRuntimeContext context, CancellationToken token) =>
        base.HandleEventAsync(message, context, token);

    protected virtual Task HandleManagerAttentionReviewAsync(AgentAttentionReviewContext review, AgentRuntimeContext context, CancellationToken token) => Task.CompletedTask;
}

public static class AgentBaseTypes
{
    public const string Manager = "manager";
    public const string IndividualContributor = "individual-contributor";
    public const string IndependentReviewer = "independent-reviewer";
    public const string ExecutiveAdvisor = "executive-advisor";

    public static string? FromPolicyProfile(string? profile) => profile switch
    {
        AgentRolePolicyProfiles.Manager => Manager,
        AgentRolePolicyProfiles.IndividualContributor => IndividualContributor,
        AgentRolePolicyProfiles.IndependentReviewer => IndependentReviewer,
        AgentRolePolicyProfiles.ExecutiveAdvisor => ExecutiveAdvisor,
        _ => null
    };
}

public sealed record ManagerIncidentContext(ManagementIncident Incident, ProjectDiagnosticsPage Diagnostics, string ActionIdempotencyKey);

public sealed record ManagerIncidentAssessment(string Disposition, string Reason, DateTimeOffset? ReviewAt = null, string? ActionReference = null)
{
    public static ManagerIncidentAssessment Escalate(string reason) => new(IncidentDispositions.Escalate, reason);
    public static ManagerIncidentAssessment Investigating(string reason, DateTimeOffset reviewAt) => new(IncidentDispositions.Investigating, reason, reviewAt);
    public static ManagerIncidentAssessment AwaitingRecovery(string reason, DateTimeOffset reviewAt, string actionReference) =>
        new(IncidentDispositions.AwaitingRecovery, reason, reviewAt, actionReference);
}

public static class IncidentDispositions
{
    public const string Escalate = "Escalate";
    public const string Investigating = "Investigating";
    public const string AwaitingRecovery = "AwaitingRecovery";
}
