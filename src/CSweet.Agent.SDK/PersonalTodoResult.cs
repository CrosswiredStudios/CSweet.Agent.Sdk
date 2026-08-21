namespace CSweet.Agent.SDK;

/// <summary>The authoring result for one personal to-do callback.</summary>
public sealed record PersonalTodoResult
{
    private PersonalTodoResult(
        bool isCompleted,
        bool keepInProgress,
        string content,
        DateTimeOffset? nextReviewAt = null,
        Guid? waitingOnOrganizationUserId = null)
    {
        IsCompleted = isCompleted;
        KeepInProgress = keepInProgress;
        Content = content;
        NextReviewAt = nextReviewAt;
        WaitingOnOrganizationUserId = waitingOnOrganizationUserId;
    }

    internal bool IsCompleted { get; }
    internal bool KeepInProgress { get; }
    internal string Content { get; }
    internal DateTimeOffset? NextReviewAt { get; }
    internal Guid? WaitingOnOrganizationUserId { get; }

    public static PersonalTodoResult Completed(string? summary = null) =>
        new(true, false, summary?.Trim() ?? string.Empty);

    /// <summary>
    /// Keeps the ticket visibly in Doing after releasing the transient execution claim. Use this
    /// when an external event, such as a manager decision, must occur before the work can complete.
    /// </summary>
    public static PersonalTodoResult InProgress(string? summary = null) =>
        new(false, true, summary?.Trim() ?? string.Empty);

    public static PersonalTodoResult WaitingUntil(
        DateTimeOffset nextReviewAt,
        string reason,
        Guid? waitingOnOrganizationUserId = null)
    {
        if (nextReviewAt <= DateTimeOffset.UtcNow)
            throw new ArgumentOutOfRangeException(nameof(nextReviewAt), "The next review must be in the future.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Waiting work requires a durable reason.", nameof(reason));
        return new(false, true, reason.Trim(), nextReviewAt, waitingOnOrganizationUserId);
    }

    public static PersonalTodoResult Blocked(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A blocked personal to-do requires a durable reason.", nameof(reason));
        return new(false, false, reason.Trim());
    }
}
