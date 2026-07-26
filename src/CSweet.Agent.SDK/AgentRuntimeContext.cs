namespace CSweet.Agent.SDK;

public sealed class AgentRuntimeContext
{
    private readonly IAgentProgressReporter _progress;

    internal AgentRuntimeContext(
        string businessId,
        string installationId,
        string runtimeInstanceId,
        string tickId,
        PlatformCapabilityClient platform,
        IAgentProgressReporter progress,
        AgentIdentity? identity = null)
    {
        BusinessId = businessId;
        InstallationId = installationId;
        RuntimeInstanceId = runtimeInstanceId;
        TickId = tickId;
        Platform = platform;
        _progress = progress;
        Identity = identity;
    }

    public string BusinessId { get; }
    public string InstallationId { get; }
    public string RuntimeInstanceId { get; }
    public string TickId { get; }

    /// <summary>
    /// The employee identity assigned by the current organization, or <see langword="null"/>
    /// when this installation has not been hired as an employee.
    /// </summary>
    public AgentIdentity? Identity { get; init; }

    public PlatformCapabilityClient Platform { get; }

    public Task ReportProgressAsync(
        object? value,
        CancellationToken cancellationToken = default) =>
        _progress.ReportAsync(value, cancellationToken);

    public Task<IReadOnlyList<Microsoft.Extensions.AI.AITool>> GetModelToolsAsync(
        CancellationToken cancellationToken = default) =>
        Platform.GetModelToolsAsync(cancellationToken);

    public Microsoft.Extensions.AI.IChatClient CreateChatClient(AgentLlmSelection selection) =>
        new PlatformChatClient(Platform, selection);
}
