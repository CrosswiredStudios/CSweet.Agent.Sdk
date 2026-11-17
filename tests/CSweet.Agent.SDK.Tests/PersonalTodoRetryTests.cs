using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class PersonalTodoRetryTests
{
    private static (AgentRuntimeContext Context, Dictionary<string, AgentOperatingStateResponse> States) Runtime()
    {
        var states = new Dictionary<string, AgentOperatingStateResponse>();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<AgentOperatingStateReadRequest, AgentOperatingStateReadResponse>(PlatformCapabilities.AgentOperatingStateRead,
                (r, _) => Task.FromResult(new AgentOperatingStateReadResponse(states.GetValueOrDefault(r.StateKey))))
            .RegisterCapability<AgentOperatingStateWriteRequest, AgentOperatingStateResponse>(PlatformCapabilities.AgentOperatingStateWrite,
                (r, _) => Task.FromResult(states[r.StateKey] = new(Guid.NewGuid(), r.StateKey, r.SchemaId, r.SchemaVersion, r.Status, r.SourceRevisions,
                    [], "", [], Guid.NewGuid(), r.Payload, (states.GetValueOrDefault(r.StateKey)?.Revision ?? 0) + 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));
        return (runtime.CreateContext(), states);
    }

    [Fact]
    public async Task TheSameFailureWaitsLongerEachTimeThenBlocksWithItsCause()
    {
        var (context, _) = Runtime();
        var item = JsonSerializer.Deserialize<PersonalTodoItem>("{}")! with { Id = Guid.NewGuid(), Revision = 3 };
        var delays = new List<TimeSpan>();
        for (var attempt = 1; attempt <= PersonalTodoRetry.Delays.Count; attempt++)
        {
            var before = DateTimeOffset.UtcNow;
            // Identifiers and counters in the message do not make it a different failure.
            var waiting = await PersonalTodoRetry.ResolveAsync(context.Platform, item,
                $"The model reply was not accepted after 3 attempt(s) for session {Guid.NewGuid()} at step {attempt}.");
            Assert.NotNull(waiting.NextReviewAt);
            Assert.Contains($"attempt {attempt} of {PersonalTodoRetry.Delays.Count}", waiting.Content);
            delays.Add(waiting.NextReviewAt!.Value - before);
        }
        Assert.Equal(delays.Order(), delays);
        Assert.True(delays[0] >= TimeSpan.FromMinutes(1) && delays[^1] >= TimeSpan.FromMinutes(30));

        var blocked = await PersonalTodoRetry.ResolveAsync(context.Platform, item,
            $"The model reply was not accepted after 3 attempt(s) for session {Guid.NewGuid()} at step 9.");
        Assert.Null(blocked.NextReviewAt); Assert.False(blocked.KeepInProgress); Assert.False(blocked.IsCompleted);
        Assert.Contains("Stopped after 5 automatic retries", blocked.Content);
        Assert.Contains("The model reply was not accepted", blocked.Content);
    }

    [Fact]
    public async Task AlternatingFailuresShareOneTotalBudgetAndOldFailuresExpire()
    {
        var (context, states) = Runtime();
        var item = JsonSerializer.Deserialize<PersonalTodoItem>("{}")! with { Id = Guid.NewGuid(), Revision = 1 };
        PersonalTodoResult last = null!;
        for (var attempt = 0; attempt <= PersonalTodoRetry.TotalLimit; attempt++)
            last = await PersonalTodoRetry.ResolveAsync(context.Platform, item, attempt % 2 == 0 ? "A platform conflict." : "Facts changed during review.");
        Assert.Null(last.NextReviewAt); Assert.Contains($"Stopped after {PersonalTodoRetry.TotalLimit} automatic retries", last.Content);

        // A failure from an earlier day starts a new budget.
        var key = states.Keys.Single();
        var old = states[key].Payload.Deserialize<PersonalTodoRetry.Budget>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!
            with { LastAt = DateTimeOffset.UtcNow - PersonalTodoRetry.Window - TimeSpan.FromMinutes(1) };
        states[key] = states[key] with { Payload = JsonSerializer.SerializeToElement(old, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        var fresh = await PersonalTodoRetry.ResolveAsync(context.Platform, item, "A platform conflict.");
        Assert.NotNull(fresh.NextReviewAt); Assert.Contains("attempt 1 of", fresh.Content);
    }

    [Fact]
    public void RetryLaterCarriesItsFailureAndIsNeitherCompleteNorBlocked()
    {
        var result = PersonalTodoResult.RetryLater("  The platform refused the update.  ");
        Assert.Equal("The platform refused the update.", result.RetryFailure);
        Assert.False(result.IsCompleted);
        Assert.Throws<ArgumentException>(() => PersonalTodoResult.RetryLater(" "));
    }
}
