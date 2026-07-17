namespace CSweet.Agent.SDK;

public enum AgentActivationReason
{
    Unknown,
    Interactive,
    Scheduled,
    Manual,
    AlwaysOnStartup
}

public sealed record AgentActivationContext(
    AgentActivationReason Reason,
    string RuntimeInstanceId,
    string TickId,
    DateTimeOffset ActivatedAt);

public interface IAgentActivationHandler
{
    Task OnActivatedAsync(
        AgentActivationContext activation,
        AgentRuntimeContext context,
        CancellationToken cancellationToken);
}
