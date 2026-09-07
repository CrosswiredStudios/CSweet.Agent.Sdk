namespace CSweet.Agent.SDK;

// Per-async-flow lease association; never expose lease credentials through agent APIs.
internal sealed class InferenceExecutionScope(AgentWorkLease lease, CancellationTokenSource deadline,
    IAgentProgressReporter progress) : IDisposable
{
    private static readonly AsyncLocal<InferenceExecutionScope?> CurrentSlot = new();
    private readonly InferenceExecutionScope? previous = CurrentSlot.Value;
    public static InferenceExecutionScope? Current => CurrentSlot.Value;
    public AgentWorkLease Lease => lease;
    public void Enter() => CurrentSlot.Value = this;
    public void UpdateDeadline(Guid workId, DateTimeOffset value)
    {
        if (workId != lease.WorkId) throw new InvalidOperationException("Inference deadline belongs to different work.");
        var remaining = value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) deadline.Cancel();
        else deadline.CancelAfter(remaining);
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
    public void Dispose() => CurrentSlot.Value = previous;
}
