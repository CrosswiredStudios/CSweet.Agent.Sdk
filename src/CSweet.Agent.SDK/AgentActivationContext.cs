namespace CSweet.Agent.SDK;

/// <summary>Reason that C-Sweet activated the current runtime tick.</summary>
public enum AgentActivationReason
{
    Unknown,
    Interactive,
    Scheduled,
    Manual,
    AlwaysOnStartup
}

/// <summary>Server-resolved activation information supplied to an optional activation handler.</summary>
public sealed record AgentActivationContext(
    AgentActivationReason Reason,
    string RuntimeInstanceId,
    string TickId,
    DateTimeOffset ActivatedAt);

/// <summary>Optional hook invoked once when an agent runtime activation is established.</summary>
public interface IAgentActivationHandler
{
    /// <summary>Handles the SDK-managed activation without taking ownership of the connection.</summary>
    Task OnActivatedAsync(
        AgentActivationContext activation,
        AgentRuntimeContext context,
        CancellationToken cancellationToken);
}
