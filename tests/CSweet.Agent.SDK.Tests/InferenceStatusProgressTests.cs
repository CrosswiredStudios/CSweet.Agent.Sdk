using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

/// <summary>
/// QFN Games, 2026-11-11: the platform rejected a model call's "request received" progress report,
/// the model call failed and the ticket was blocked. Queue-state progress is display telemetry.
/// </summary>
public sealed class InferenceStatusProgressTests
{
    [Fact]
    public async Task A_rejected_status_report_does_not_fail_the_model_call()
    {
        var reporter = new RejectingReporter();
        using var deadline = new CancellationTokenSource();
        using var scope = new InferenceExecutionScope(Lease(), deadline, reporter);

        await scope.ReportStatusAsync("Received", CancellationToken.None);
        await scope.ReportStatusAsync("Queued", CancellationToken.None);

        Assert.Equal(2, reporter.Calls);
    }

    [Fact]
    public async Task Cancellation_still_stops_the_model_call()
    {
        using var deadline = new CancellationTokenSource();
        using var scope = new InferenceExecutionScope(Lease(), deadline, new RejectingReporter());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.ReportStatusAsync("Queued", cancelled.Token));
    }

    private static AgentWorkLease Lease() =>
        new(Guid.NewGuid(), 1, AgentWorkKind.Capability, "work.execution.run", JsonSerializer.SerializeToElement(new { }),
            "lease", DateTimeOffset.UtcNow.AddMinutes(1), DateTimeOffset.UtcNow.AddHours(1), null, null);

    private sealed class RejectingReporter : IAgentProgressReporter
    {
        public int Calls;

        public Task ReportAsync(object? value, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                "The MCP service rejected the request: Progress sequence numbers must be strictly monotonic without gaps.");
        }
    }
}
