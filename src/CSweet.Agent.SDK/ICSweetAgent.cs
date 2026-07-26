namespace CSweet.Agent.SDK;

public interface ICSweetAgent
{
    string AgentId { get; }

    string Version { get; }

    Task HandleEventAsync(
        AgentEventEnvelope message,
        AgentRuntimeContext context,
        CancellationToken cancellationToken);

    Task<AgentWorkResult> ExecuteCapabilityAsync(
        AgentCapabilityRequest request,
        AgentRuntimeContext context,
        CancellationToken cancellationToken);
}

/// <summary>Optional lifecycle for always-on plugins that need to run while their MCP runtime session is connected.</summary>
public interface IAgentConnectedService
{
    Task RunConnectedAsync(AgentRuntimeContext context, CancellationToken cancellationToken);
}
