namespace CSweet.Agent.SDK;

/// <summary>The authoring result for one personal to-do callback.</summary>
public sealed record PersonalTodoResult
{
    private PersonalTodoResult(bool isCompleted, string content)
    {
        IsCompleted = isCompleted;
        Content = content;
    }

    internal bool IsCompleted { get; }
    internal string Content { get; }

    public static PersonalTodoResult Completed(string? summary = null) =>
        new(true, summary?.Trim() ?? string.Empty);

    public static PersonalTodoResult Blocked(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A blocked personal to-do requires a durable reason.", nameof(reason));
        return new(false, reason.Trim());
    }
}
