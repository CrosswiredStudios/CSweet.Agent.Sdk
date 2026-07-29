namespace CSweet.Agent.SDK;

/// <summary>The transport-neutral callback contract implemented by every C-Sweet agent.</summary>
public interface ICSweetAgent
{
    /// <summary>Gets the stable package identity. It must match the root manifest.</summary>
    string AgentId { get; }

    /// <summary>Gets the semantic package version. It must match the root manifest.</summary>
    string Version { get; }

    /// <summary>Handles one durable event subscription delivery.</summary>
    Task HandleEventAsync(
        AgentEventEnvelope message,
        AgentRuntimeContext context,
        CancellationToken cancellationToken);

    /// <summary>Executes one provided capability request and returns its terminal result.</summary>
    Task<AgentWorkResult> ExecuteCapabilityAsync(
        AgentCapabilityRequest request,
        AgentRuntimeContext context,
        CancellationToken cancellationToken);
}

/// <summary>Optional lifecycle for always-on plugins that need to run while their MCP runtime session is connected.</summary>
public interface IAgentConnectedService
{
    /// <summary>Runs while the current SDK-managed runtime session remains connected.</summary>
    Task RunConnectedAsync(AgentRuntimeContext context, CancellationToken cancellationToken);
}
