namespace CSweet.Agent.SDK;

// Per-async-flow lease association; never expose lease credentials through agent APIs.
internal sealed class InferenceExecutionScope(AgentWorkLease lease, CancellationTokenSource deadline,
    IAgentProgressReporter progress) : IDisposable
{
    private static readonly AsyncLocal<InferenceExecutionScope?> CurrentSlot = new();
    private readonly InferenceExecutionScope? previous = CurrentSlot.Value;
    private readonly CancellationTokenSource ended = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
    private int closed;
    public static InferenceExecutionScope? Current => CurrentSlot.Value;
    public AgentWorkLease Lease => lease;
    internal CancellationToken Token => ended.Token;
    internal PlatformCapabilityClient? Platform { get; private set; }
    public void Enter()
    {
        if (Volatile.Read(ref closed) != 0) throw new InvalidOperationException("The work context has ended.");
        CurrentSlot.Value = this;
    }
    internal PlatformCapabilityClient BindPlatform(IPlatformToolInvoker tools) =>
        Platform = new(new WorkScopedToolInvoker(tools, this));
    internal void RequireActive(string capability)
    {
        if (Volatile.Read(ref closed) != 0 || ended.IsCancellationRequested)
            throw new PlatformCapabilityException(capability, PlatformCapabilityErrorCode.Denied,
                "This callback's work context has ended. Continue through a new authorized work callback.",
                failureCode: "agent.context_ended", retryable: false);
        if (!ReferenceEquals(CurrentSlot.Value, this))
            throw new PlatformCapabilityException(capability, PlatformCapabilityErrorCode.Denied,
                "This platform client belongs to another work callback.",
                failureCode: "agent.context_mismatch", retryable: false);
    }
    public void Close()
    {
        if (Interlocked.Exchange(ref closed, 1) != 0) return;
        // Cancellation callbacks cannot reopen the context or prevent its fence from closing.
        try { ended.Cancel(); } catch (AggregateException) { }
    }
    public void UpdateDeadline(Guid workId, DateTimeOffset value)
    {
        if (workId != lease.WorkId) throw new InvalidOperationException("Inference deadline belongs to different work.");
        var remaining = value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) deadline.Cancel();
        // Personal work has no domain deadline. CancelAfter cannot represent that range;
        // disable any previous finite timer while keeping linked runtime cancellation active.
        else deadline.CancelAfter(remaining <= TimeSpan.FromMilliseconds(uint.MaxValue - 1d)
            ? remaining : Timeout.InfiniteTimeSpan);
    }
    public Task ReportStatusAsync(string state, CancellationToken token) => progress.ReportAsync(new
    {
        Delta = state switch
        {
            "Received" => "LLM request received",
            "Queued" => "Waiting for an available LLM provider slot",
            "Generating" => "LLM provider is processing the request",
            "Completed" => "LLM response received",
            _ => "LLM request ended"
        },
        Kind = state is "Completed" or "Failed" or "Cancelled" ? AgentTurnStreamKinds.ActivityCompleted : AgentTurnStreamKinds.ActivityStarted,
        IsFinal = false,
        Metadata = new Dictionary<string, string> { ["llmState"] = state }
    }, token);
    public void Dispose()
    {
        Close();
        if (ReferenceEquals(CurrentSlot.Value, this)) CurrentSlot.Value = previous;
        ended.Dispose();
    }
}
