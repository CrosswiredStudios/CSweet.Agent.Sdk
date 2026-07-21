namespace CSweet.Agent.SDK;

public sealed record AgentRuntimeContext(
    string BusinessId,
    string InstallationId,
    IAgentBrokerClient Broker,
    string RuntimeInstanceId = "",
    string TickId = "")
{
    /// <summary>
    /// The employee identity assigned by the current organization, or <see langword="null"/>
    /// when this installation has not been hired as an employee.
    /// </summary>
    public AgentIdentity? Identity { get; init; }

    public PlatformCapabilityClient Platform => new(Broker);
}
