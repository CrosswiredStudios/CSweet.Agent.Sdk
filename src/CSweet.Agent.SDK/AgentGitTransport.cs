using System.Text.RegularExpressions;

namespace CSweet.Agent.SDK;

/// <summary>One buffered Git smart-HTTP exchange for an assigned workspace.</summary>
internal sealed record AgentGitExchange(
    string Method,
    string PathAndQuery,
    IReadOnlyDictionary<string, string> Headers,
    ReadOnlyMemory<byte> Body);

internal sealed record AgentGitExchangeResult(int StatusCode, string? ContentType, byte[] Body);

/// <summary>
/// Sends a Git exchange through the authenticated runtime session. The implementation adds the
/// session credentials; callers never see them.
/// </summary>
internal interface IAgentGitTransport
{
    Task<AgentGitExchangeResult> ExchangeGitAsync(AgentGitExchange exchange, CancellationToken cancellationToken);
}

/// <summary>
/// The only Git routes the platform serves to a runtime. The guest broker proxy and headquarters
/// apply the same shapes; this copy rejects everything else before it leaves the agent process.
/// </summary>
internal static class AgentGitRoutes
{
    /// <summary>The guest broker proxy's body limit for one buffered exchange.</summary>
    internal const int MaximumBodyBytes = 16 * 1024 * 1024 - 64 * 1024;

    internal static readonly IReadOnlySet<string> ForwardedRequestHeaders =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Accept", "Content-Type", "Content-Encoding", "Git-Protocol" };

    private static readonly Regex Advertisement = new(
        @"\A/git/workspaces/[0-9a-fA-F-]{36}/[1-9][0-9]{0,18}/info/refs\?service=git-(upload|receive)-pack\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex Exchange = new(
        @"\A/git/workspaces/[0-9a-fA-F-]{36}/[1-9][0-9]{0,18}/git-(upload|receive)-pack\z",
        RegexOptions.CultureInvariant);

    internal static bool IsSupported(string method, string pathAndQuery) => method switch
    {
        "GET" => Advertisement.IsMatch(pathAndQuery),
        "POST" => Exchange.IsMatch(pathAndQuery),
        _ => false
    };

    internal static string WorkspacePath(Guid workspaceId, long assignmentRevision)
    {
        if (workspaceId == Guid.Empty || assignmentRevision < 1)
            throw new ArgumentException("A current workspace is required.");
        return $"/git/workspaces/{workspaceId:D}/{assignmentRevision}";
    }
}
