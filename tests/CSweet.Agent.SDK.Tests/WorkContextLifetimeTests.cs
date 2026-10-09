using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class WorkContextLifetimeTests
{
    [Fact]
    public async Task RetainedClientCannotInvokeFromAnotherCallbackOrAfterCompletion()
    {
        using var deadline = new CancellationTokenSource();
        using var first = Scope(deadline);
        first.Enter();
        var transport = new Invoker();
        var client = first.BindPlatform(transport);
        var accessor = new AgentPlatformAccessor();
        accessor.SetCurrent(new PlatformCapabilityClient(transport));
        Assert.Same(client, accessor.Current);
        await client.Tools.InvokeAsync("tool", Json(), default);
        using (var second = Scope(deadline))
        {
            second.Enter();
            second.BindPlatform(transport);
            Assert.NotSame(client, accessor.Current);
            var mismatch = await Assert.ThrowsAsync<PlatformCapabilityException>(() => client.Tools.InvokeAsync("tool", Json()));
            Assert.Equal("agent.context_mismatch", mismatch.FailureCode);
            Assert.False(mismatch.Retryable);
        }
        Assert.Same(client, accessor.Current);
        first.Close();
        var ended = await Assert.ThrowsAsync<PlatformCapabilityException>(() => client.Tools.InvokeAsync("tool", Json()));
        Assert.Equal("agent.context_ended", ended.FailureCode);
        Assert.False(ended.Retryable);
        Assert.Throws<PlatformCapabilityException>(() => accessor.Current);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task InheritedBackgroundFlowRemainsFencedAfterParentDisposes()
    {
        using var deadline = new CancellationTokenSource();
        var scope = Scope(deadline);
        scope.Enter();
        var transport = new Invoker();
        var client = scope.BindPlatform(transport);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = Task.Run(async () =>
        {
            await release.Task;
            Assert.Same(scope, InferenceExecutionScope.Current);
            return await Assert.ThrowsAsync<PlatformCapabilityException>(() => client.Tools.InvokeAsync("tool", Json()));
        });
        scope.Dispose();
        Assert.Null(InferenceExecutionScope.Current);
        release.SetResult();
        Assert.Equal("agent.context_ended", (await child).FailureCode);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task CachedActivationClientAlsoHonorsInheritedClosedFlow()
    {
        var transport = new Invoker();
        var sessionClient = new PlatformCapabilityClient(transport);
        await sessionClient.Tools.InvokeAsync("tool", Json());
        using var deadline = new CancellationTokenSource();
        using var scope = Scope(deadline);
        scope.Enter();
        scope.Close();
        var failure = await Assert.ThrowsAsync<PlatformCapabilityException>(() => sessionClient.Tools.InvokeAsync("tool", Json()));
        Assert.Equal("agent.context_ended", failure.FailureCode);
        Assert.Equal(1, transport.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingContextCancelsOutstandingCallsAndWithholdsLateResults(bool descriptors)
    {
        using var deadline = new CancellationTokenSource();
        using var scope = Scope(deadline);
        scope.Enter();
        var transport = new Invoker { Wait = true };
        var client = scope.BindPlatform(transport);
        var call = descriptors ? (Task)client.Tools.ListToolsAsync() : client.Tools.InvokeAsync("tool", Json());
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scope.Close();
        Assert.True(transport.LastToken.IsCancellationRequested);
        // Deliberately ignore cancellation, as a late server response might do.
        transport.Release.SetResult();
        var failure = await Assert.ThrowsAsync<PlatformCapabilityException>(async () => await call);
        Assert.Equal("agent.context_ended", failure.FailureCode);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task StreamingCannotReleaseChunksAfterContextCloses()
    {
        using var deadline = new CancellationTokenSource();
        using var scope = Scope(deadline);
        scope.Enter();
        var transport = new Invoker();
        var client = scope.BindPlatform(transport);
        await using var stream = client.Tools.InvokeStreamingAsync("tool", Json()).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        scope.Close();
        Assert.True(transport.LastToken.IsCancellationRequested);
        var failure = await Assert.ThrowsAsync<PlatformCapabilityException>(async () => await stream.MoveNextAsync());
        Assert.Equal("agent.context_ended", failure.FailureCode);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task CancelledDeadlineFencesNewCallsWithoutRuntimeShutdown()
    {
        using var deadline = new CancellationTokenSource();
        using var scope = Scope(deadline);
        scope.Enter();
        var transport = new Invoker();
        var client = scope.BindPlatform(transport);
        deadline.Cancel();
        var failure = await Assert.ThrowsAsync<PlatformCapabilityException>(() => client.Tools.InvokeAsync("tool", Json()));
        Assert.Equal("agent.context_ended", failure.FailureCode);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task ClosedCallbackCannotPublishProgress()
    {
        using var deadline = new CancellationTokenSource();
        using var scope = Scope(deadline);
        scope.Enter();
        var recorder = new Progress();
        var reporter = new WorkScopedProgressReporter(recorder, scope);
        await reporter.ReportAsync(new { ready = true });
        scope.Close();
        var failure = await Assert.ThrowsAsync<PlatformCapabilityException>(() => reporter.ReportAsync(new { late = true }));
        Assert.Equal("agent.context_ended", failure.FailureCode);
        Assert.Equal(1, recorder.Calls);
    }

    private static InferenceExecutionScope Scope(CancellationTokenSource deadline) => new(new(Guid.NewGuid(), 1,
        AgentWorkKind.Event, "test", Json(), "lease", DateTimeOffset.UtcNow.AddMinutes(1), DateTimeOffset.UtcNow.AddHours(1),
        Guid.NewGuid(), null), deadline, new Progress());
    private static JsonElement Json() => JsonSerializer.SerializeToElement(new { });

    private sealed class Progress : IAgentProgressReporter
    {
        public int Calls;
        public Task ReportAsync(object? value, CancellationToken cancellationToken = default)
        { Calls++; return Task.CompletedTask; }
    }

    private sealed class Invoker : IPlatformToolInvoker
    {
        public int Calls;
        public bool Wait;
        public CancellationToken LastToken;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private async Task Start(CancellationToken token)
        {
            Calls++;
            LastToken = token;
            Started.TrySetResult();
            if (Wait) await Release.Task;
        }
        public async Task<JsonElement> InvokeAsync(string capability, JsonElement arguments, CancellationToken cancellationToken = default)
        { await Start(cancellationToken); return Json(); }
        public async Task<IReadOnlyList<AgentToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default)
        { await Start(cancellationToken); return []; }
        public async IAsyncEnumerable<JsonElement> InvokeStreamingAsync(string capability, JsonElement arguments,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await Start(cancellationToken); yield return Json(); yield return Json(); }
    }
}
