namespace CSweet.Agent.SDK;

public static class AgentAttentionEvents
{
    public const string ReviewDue = "com.csweet.agent.attention.review-due.v1";
}

public static class AgentAttentionReasons
{
    public const string Startup = "Startup";
    public const string Periodic = "Periodic";
    public const string Recovered = "Recovered";
}

/// <summary>A platform-issued request to reconcile durable commitments.</summary>
public sealed record AgentAttentionReviewDueEvent(
    Guid ReviewId,
    DateTimeOffset OccurredAt,
    DateTimeOffset NextReviewAt,
    string Reason);

public sealed record AgentAttentionReviewContext(
    Guid ReviewId,
    DateTimeOffset OccurredAt,
    DateTimeOffset NextReviewAt,
    string Reason);
