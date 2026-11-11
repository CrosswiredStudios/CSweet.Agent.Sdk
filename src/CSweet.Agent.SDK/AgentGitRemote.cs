namespace CSweet.Agent.SDK;

/// <summary>What a git child process needs to reach one assigned workspace: a remote URL without a
/// credential, and the environment that carries the forwarder secret.</summary>
internal sealed record AgentGitRemoteAccess(string RemoteUrl, IReadOnlyDictionary<string, string> Environment);

/// <summary>Implemented by transports that can carry Git for an assigned workspace.</summary>
internal interface IAgentGitRemote
{
    /// <summary>Returns <see langword="null"/> when this transport has no Git route.</summary>
    AgentGitRemoteAccess? OpenGitRemote(Guid workspaceId, long assignmentRevision);
}
