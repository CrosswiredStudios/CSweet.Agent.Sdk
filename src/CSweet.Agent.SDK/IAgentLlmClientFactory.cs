using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK;

public sealed record AgentLlmSelection(
    Guid ProviderProfileId,
    string? Model = null,
    AgentLlmInvocationContext? Invocation = null);

public sealed record AgentLlmInvocationContext(
    Guid? ConversationId = null,
    Guid? ChatTurnId = null,
    string InvocationKind = "agent-inference");

public interface IAgentLlmClientFactory
{
    Task<IChatClient> CreateChatClientAsync(
        AgentLlmSelection selection,
        CancellationToken cancellationToken = default);
}
