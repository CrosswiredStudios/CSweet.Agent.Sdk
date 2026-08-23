namespace CSweet.Agent.SDK.Tests;

public sealed class AgentTurnStreamWriterTests
{
    [Fact]
    public async Task Writes_ordered_coalesced_events_and_authoritative_commit()
    {
        var runtime = new AgentTestRuntime();
        var turnId = Guid.NewGuid();

        await using (var stream = runtime.CreateContext().CreateTurnStream("conversation-1", turnId, 2, "Confidential"))
        {
            await stream.ActivityStartedAsync("Working");
            await stream.WriteReasoningAsync("reason ");
            await stream.WriteReasoningAsync("continued");
            await stream.CompleteReasoningAsync();
            await stream.WriteDraftAsync("old draft");
            await stream.ResetDraftAsync("validation retry");
            await stream.WriteDraftAsync("new draft");
            await stream.CommitAsync("validated answer");
        }

        Assert.Equal(
            [
                AgentTurnStreamKinds.ActivityStarted,
                AgentTurnStreamKinds.ReasoningDelta,
                AgentTurnStreamKinds.ReasoningCompleted,
                AgentTurnStreamKinds.DraftReset,
                AgentTurnStreamKinds.DraftDelta,
                AgentTurnStreamKinds.FinalCommit
            ],
            runtime.Progress.Select(item => item.GetProperty("kind").GetString()));
        Assert.Equal(
            Enumerable.Range(0, runtime.Progress.Count),
            runtime.Progress.Select(item => item.GetProperty("sequence").GetInt32()));
        Assert.All(runtime.Progress, item =>
        {
            Assert.Equal(turnId, item.GetProperty("turnId").GetGuid());
            Assert.Equal(2, item.GetProperty("attempt").GetInt32());
            Assert.Equal("Confidential", item.GetProperty("sensitivity").GetString());
        });
        Assert.Equal(
            "validated answer",
            runtime.Progress[^1].GetProperty("delta").GetString());
        Assert.True(runtime.Progress[^1].GetProperty("isFinal").GetBoolean());
    }

    [Fact]
    public async Task Rejects_events_after_terminal_failure()
    {
        var runtime = new AgentTestRuntime();
        await using var stream = runtime.CreateContext().CreateTurnStream("conversation-1", Guid.NewGuid());

        await stream.FailAsync("safe failure");

        await Assert.ThrowsAsync<InvalidOperationException>(() => stream.WriteDraftAsync("late"));
        Assert.Equal(AgentTurnStreamKinds.TurnFailed, runtime.Progress.Single().GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Activity_scope_records_duration_and_terminal_status()
    {
        var runtime = new AgentTestRuntime();
        await using var stream = runtime.CreateContext().CreateTurnStream("conversation-1", Guid.NewGuid());

        await using (var activity = await stream.StartActivityAsync("Calling tool"))
            await activity.CompleteAsync("Tool completed", new Dictionary<string, string> { ["tool"] = "search" });

        Assert.Equal(AgentTurnStreamKinds.ActivityStarted, runtime.Progress[0].GetProperty("kind").GetString());
        Assert.Equal(AgentTurnStreamKinds.ActivityCompleted, runtime.Progress[1].GetProperty("kind").GetString());
        var metadata = runtime.Progress[1].GetProperty("metadata");
        Assert.Equal("search", metadata.GetProperty("tool").GetString());
        Assert.True(long.TryParse(metadata.GetProperty("durationMs").GetString(), out _));
    }
}
