namespace CSweet.Agent.SDK;

/// <summary>
/// Server-bound identity, typed platform access, progress, and model helpers for one callback.
/// </summary>
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

    /// <summary>Gets the server-resolved organization identity.</summary>
    public string BusinessId { get; }
    /// <summary>Gets the server-resolved installation identity.</summary>
    public string InstallationId { get; }
    /// <summary>Gets the current runtime instance identity.</summary>
    public string RuntimeInstanceId { get; }
    /// <summary>Gets the current activation tick identity.</summary>
    public string TickId { get; }

    /// <summary>
    /// The employee identity assigned by the current organization, or <see langword="null"/>
    /// when this installation has not been hired as an employee.
    /// </summary>
    public AgentIdentity? Identity { get; init; }

    /// <summary>Gets typed, live-grant-governed access to C-Sweet platform services.</summary>
    public PlatformCapabilityClient Platform { get; }

    /// <summary>Appends bounded progress to the current durable work attempt.</summary>
    public Task ReportProgressAsync(
        object? value,
        CancellationToken cancellationToken = default) =>
        _progress.ReportAsync(value, cancellationToken);

    /// <summary>Returns only tools that are currently granted and model-visible.</summary>
    public Task<IReadOnlyList<Microsoft.Extensions.AI.AITool>> GetModelToolsAsync(
        CancellationToken cancellationToken = default) =>
        Platform.GetModelToolsAsync(cancellationToken);

    /// <summary>
    /// Resolves exactly the named approved model-visible bindings for a confined harness and fails
    /// closed when any binding is absent, duplicated, or not model-visible.
    /// </summary>
    public Task<IReadOnlyList<Microsoft.Extensions.AI.AITool>> GetModelToolsAsync(
        IReadOnlyCollection<string> capabilities,
        CancellationToken cancellationToken = default) =>
        Platform.GetModelToolsAsync(capabilities, cancellationToken);

    /// <summary>Creates a platform-governed model client without exposing provider credentials.</summary>
    public Microsoft.Extensions.AI.IChatClient CreateChatClient(AgentLlmSelection selection) =>
        new PlatformChatClient(Platform, selection);

    /// <summary>Creates an ordered durable stream for one interactive chat turn.</summary>
    public AgentTurnStreamWriter CreateTurnStream(
        string conversationId,
        Guid turnId,
        int attempt = 0,
        string sensitivity = "Internal") =>
        new(_progress, conversationId, turnId, attempt, sensitivity);
}
