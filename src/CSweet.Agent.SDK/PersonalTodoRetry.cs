using System.Security.Cryptography;
using System.Text;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>
/// Bounded automatic retry for a personal to-do attempt that failed for a reason that may clear without
/// anyone acting: a rejected model reply, a platform conflict, facts that changed during the attempt, or an
/// unexpected exception. The same failure is retried with increasing delay. When the budget is spent the
/// item is blocked with the exact failure, so its manager sees what actually went wrong.
/// </summary>
public static class PersonalTodoRetry
{
    /// <summary>Durable retry record for one personal to-do.</summary>
    public sealed record Budget(string Signature, int Attempts, int Total, DateTimeOffset LastAt);

    /// <summary>Delay before each retry of the same failure. Its length is the per-failure retry limit.</summary>
    public static readonly IReadOnlyList<TimeSpan> Delays =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30)
    ];

    /// <summary>Retries across all failures within <see cref="Window"/>, so alternating failures cannot loop.</summary>
    public const int TotalLimit = 10;

    /// <summary>Failures older than this no longer count against the budget.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(6);

    internal static string StateKey(Guid itemId) => "personal-todo-retry:v1:" + itemId.ToString("N");

    /// <summary>A stable identity for a failure. Identifiers and numbers are ignored so the same fault matches itself.</summary>
    public static string Signature(string failure)
    {
        var text = IdentifierPattern.Replace(failure.ToLowerInvariant(), "#");
        var normalized = new StringBuilder(text.Length);
        foreach (var character in text)
            if (!char.IsDigit(character)) normalized.Append(character);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToString())))[..32];
    }

    private static readonly System.Text.RegularExpressions.Regex IdentifierPattern = new(
        "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|[0-9a-f]{16,}",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    /// <summary>One line naming the failure for the item's wait or block reason.</summary>
    public static string Describe(Exception error)
    {
        var text = error.GetType().Name + ": " + error.Message.ReplaceLineEndings(" ").Trim();
        return text.Length <= 600 ? text : text[..600];
    }

    /// <summary>
    /// Records this failure and returns the outcome: wait and retry, or block once the budget is spent.
    /// </summary>
    public static async Task<PersonalTodoResult> ResolveAsync(PlatformCapabilityClient platform, PersonalTodoItem item,
        string failure, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(failure)) throw new ArgumentException("A retry needs the failure it follows.", nameof(failure));
        failure = failure.Trim();
        var signature = Signature(failure);
        var now = DateTimeOffset.UtcNow;
        var saved = await DurableAgentState.UpdateAsync<Budget>(platform, StateKey(item.Id), "personal-todo.retry.v1", 1, prior =>
        {
            if (prior is not null && now - prior.LastAt > Window) prior = null;
            return new Budget(signature, prior?.Signature == signature ? prior.Attempts + 1 : 1, (prior?.Total ?? 0) + 1, now);
        }, ct);
        var budget = saved.Payload;
        if (budget.Attempts > Delays.Count || budget.Total > TotalLimit)
            return PersonalTodoResult.Blocked(
                $"Stopped after {budget.Total - 1} automatic retries without success. Last failure: {failure}");
        return PersonalTodoResult.WaitingUntil(now + Delays[budget.Attempts - 1],
            $"Retrying automatically (attempt {budget.Attempts} of {Delays.Count}) after: {failure}");
    }
}
