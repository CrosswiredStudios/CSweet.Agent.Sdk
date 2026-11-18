using System.Text.Json;
using CSweet.Agent.SDK;
using CSweet.Agent.Testing;
using Xunit;

namespace CSweet.Agent.SDK.Tests;

public sealed class OperatingStateFixtureTests
{
    [Fact]
    public async Task SeedPreservesCheckpointAcrossContextsAndDetachesCallerOwnedData()
    {
        var runtime = new AgentTestRuntime();
        var states = new OperatingStateFixture(runtime);
        var platform = runtime.CreateContext().Platform;
        Assert.Null(await platform.ReadOperatingStateAsync(new("pitch")));
        var sources = new Dictionary<string, string> { ["brief"] = "v7" };
        var now = DateTimeOffset.UtcNow.AddHours(-1);
        using (var json = JsonDocument.Parse("""{"stage":"review"}"""))
        {
            states.Seed(new AgentOperatingStateResponse(Guid.NewGuid(), "pitch", "pitch.v2", 2, "Review",
                sources, ["has-brief"], "exact", ["ask-owner"], Guid.NewGuid(), json.RootElement, 7, now, now));
        }
        sources.Clear();
        var restored = Assert.IsType<AgentOperatingStateResponse>(
            await runtime.CreateContext().Platform.ReadOperatingStateAsync(new("pitch")));
        Assert.Equal(7, restored.Revision);
        Assert.Equal("v7", restored.SourceRevisions["brief"]);
        Assert.Equal("review", restored.Payload.GetProperty("stage").GetString());
        var next = await platform.WriteOperatingStateAsync(Write("publish", 7));
        Assert.Equal(restored.Id, next.Id);
        Assert.Equal(now, next.CreatedAt);
        Assert.Equal(8, next.Revision);
        Assert.Throws<ArgumentException>(() => states.Seed("pitch", new { stage = "overwrite" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => states.Seed("other", new { }, 0));
        Assert.Throws<ArgumentException>(() => states.Seed("other", "invalid payload"));
        var separate = new AgentTestRuntime();
        _ = new OperatingStateFixture(separate);
        Assert.Null(await separate.CreateContext().Platform.ReadOperatingStateAsync(new("pitch")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public async Task ReplaysOriginalReceiptEvenAfterLaterWrites(long? expected)
    {
        var runtime = new AgentTestRuntime();
        _ = new OperatingStateFixture(runtime);
        var platform = runtime.CreateContext().Platform;
        var write = Write("first", expected);
        var first = await platform.WriteOperatingStateAsync(write);
        var second = await platform.WriteOperatingStateAsync(write with { ExpectedRevision = 1, IdempotencyKey = "second" });
        var replay = await platform.WriteOperatingStateAsync(write with
        {
            ExpectedRevision = 99, AttentionReviewId = Guid.NewGuid(),
            Payload = JsonSerializer.SerializeToElement(new { title = "demo", count = 1 })
        });
        Assert.Equal(1, replay.Revision);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(first.UpdatedAt, replay.UpdatedAt);
        Assert.Equal(first.AttentionReviewId, replay.AttentionReviewId);
        var conflict = await Assert.ThrowsAsync<PlatformCapabilityException>(() =>
            platform.WriteOperatingStateAsync(write with { DecisionFingerprint = "different" }));
        Assert.Equal(PlatformCapabilityErrorCode.Conflict, conflict.Code);
        Assert.StartsWith("idempotency.content_mismatch", conflict.Message);
        var current = await platform.ReadOperatingStateAsync(new("pitch"));
        Assert.Equal(second.Revision, current!.Revision);
        var another = await platform.WriteOperatingStateAsync(write with { StateKey = "other" });
        Assert.Equal(1, another.Revision);
        Assert.NotEqual(first.Id, another.Id);
    }

    [Fact]
    public async Task ChecksRevisionsAndAcceptsOneOfTwoCompetingUpdates()
    {
        var runtime = new AgentTestRuntime();
        var states = new OperatingStateFixture(runtime);
        var platform = runtime.CreateContext().Platform;
        var missing = await Assert.ThrowsAsync<PlatformCapabilityException>(() =>
            platform.WriteOperatingStateAsync(Write("update", 2)));
        Assert.Equal(PlatformCapabilityErrorCode.Conflict, missing.Code);
        states.Seed("pitch", new { count = 0 }, 3);
        foreach (var expected in new long?[] { null, 0, 2, 4 })
        {
            var stale = await Assert.ThrowsAsync<PlatformCapabilityException>(() =>
                platform.WriteOperatingStateAsync(Write("update", expected)));
            Assert.Equal(PlatformCapabilityErrorCode.Conflict, stale.Code);
        }
        var attempts = new[] { "left", "right" }.Select(key => Task.Run(async () =>
            await Record.ExceptionAsync(() => platform.WriteOperatingStateAsync(Write(key, 3))))).ToArray();
        var results = await Task.WhenAll(attempts);
        Assert.Single(results, x => x is null);
        Assert.Equal(PlatformCapabilityErrorCode.Conflict,
            Assert.IsType<PlatformCapabilityException>(Assert.Single(results, x => x is not null)).Code);
        Assert.Equal(4, (await platform.ReadOperatingStateAsync(new("pitch")))!.Revision);
    }

    private static AgentOperatingStateWriteRequest Write(string key, long? expected) =>
        new("pitch", "pitch.v1", 1, "Active", new Dictionary<string, string>(), [], "decision", [],
            Guid.NewGuid(), JsonSerializer.SerializeToElement(new { count = 1, title = "demo" }), expected, key);
}
