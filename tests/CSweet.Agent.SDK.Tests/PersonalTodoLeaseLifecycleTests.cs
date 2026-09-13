using System.Runtime.CompilerServices;
using System.Text.Json;
using CSweet.Agent.Contracts.Packaging;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CSweet.Agent.SDK.Tests;

public sealed class PersonalTodoLeaseLifecycleTests
{
    [Fact]
    public async Task StartupWaitsForDurableDeliveryThenRunsPersonalWorkWithProgressAndInferenceScope()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, """
        {
          "manifestVersion":"2.0","kind":"agent","id":"com.example.recovery",
          "name":"Recovery","version":"1.0.0","publisher":{"id":"example","name":"Example"},
          "rolePolicy":{"profile":"individual-contributor.v1","declaredRoleKeys":["software-developer"]},
          "runtime":{"type":"dotnet-project","projectPath":"src/Example.csproj","targetFramework":"net10.0","defaultActivationMode":"OnDemand"},
          "protocol":{"minimumVersion":"2.0","maximumVersion":"2.x"},
          "provides":[],"requires":[{"name":"work.personal-todo.claim.v1"}],
          "events":{"subscribes":["com.csweet.work.personal-todo.available.v1"]}
        }
        """);
        // Use the published constant so this test exercises the real startup subscription.
        var manifest = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, manifest.Replace("work.personal-todo.claim.v1", PersonalTodoCapabilities.Claim));
        await using var transport = new Transport();
        using var worker = new AgentRuntimeWorker<TestAgent>(new TestAgent(), transport,
            new AgentPlatformAccessor(), Options.Create(new AgentRuntimeOptions { ManifestPath = path }),
            NullLogger<AgentRuntimeWorker<TestAgent>>.Instance);
        try
        {
            await worker.StartAsync(default);
            await transport.WaitingForDelivery.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, transport.PersonalClaims);
            transport.DeliveryAvailable.TrySetResult();
            await transport.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(transport.Failures);
            Assert.Equal(1, transport.CompletedItems);
            Assert.Equal(1, transport.InferenceCalls);
            Assert.Equal(new long[] { 1, 2 }, transport.ProgressSequences);
        }
        finally
        {
            await worker.StopAsync(default);
            File.Delete(path);
        }
    }

    private sealed class TestAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.recovery";
        public override string Version => "1.0.0";
        public override async Task<PersonalTodoResult> HandlePersonalTodoAsync(
            PersonalTodoItem item, AgentRuntimeContext context, CancellationToken cancellationToken)
        {
            await context.ReportProgressAsync(new { stage = "coding" }, cancellationToken);
            using var client = context.CreateChatClient(new(Guid.NewGuid(), "test-model"));
            var response = await client.GetResponseAsync("Build the application", cancellationToken: cancellationToken);
            Assert.Equal("Implemented", response.Text);
            await context.ReportProgressAsync(new { stage = "tested" }, cancellationToken);
            return PersonalTodoResult.Completed("Built and tested");
        }
    }

    private sealed class Transport : IAgentRuntimeTransport
    {
        public TaskCompletionSource WaitingForDelivery { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DeliveryAvailable { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Failures { get; } = [];
        public List<long> ProgressSequences { get; } = [];
        public int PersonalClaims, CompletedItems, InferenceCalls;
        private bool delivered;
        private readonly Guid eventId = Guid.NewGuid();
        private readonly Guid workId = Guid.NewGuid();
        private readonly PersonalTodoItem item = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Daniel", "Build an application", "", PersonalTodoStatuses.Running, WorkPriorities.Medium,
            1024, 1, null, null, null, [], null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public AgentRuntimeSession? Session { get; private set; }
        public Task<AgentRuntimeSession> InitializeAsync(AgentManifest manifest, CancellationToken ct) =>
            Task.FromResult(Session = new("session", DateTimeOffset.UtcNow.AddHours(1), 1, null, null));
        public async Task<AgentWorkLease?> ClaimAsync(int maximumItems, CancellationToken ct)
        {
            WaitingForDelivery.TrySetResult();
            await DeliveryAvailable.Task.WaitAsync(ct);
            if (delivered) { await Task.Delay(Timeout.Infinite, ct); return null; }
            delivered = true;
            return new(workId, 1, AgentWorkKind.Event, PersonalTodoEvents.Available,
                JsonSerializer.SerializeToElement(new { }), "lease", DateTimeOffset.UtcNow.AddMinutes(1),
                DateTimeOffset.UtcNow.AddHours(1), eventId, null);
        }
        public Task<JsonElement> InvokeAsync(string capability, JsonElement arguments, CancellationToken cancellationToken = default)
        {
            Assert.Equal(workId, InferenceExecutionScope.Current?.Lease.WorkId);
            Assert.Equal(eventId, arguments.GetProperty("eventId").GetGuid());
            object result = capability switch
            {
                var c when c == PersonalTodoCapabilities.Claim => new PersonalTodoClaim(PersonalClaims++ == 0 ? item : null),
                var c when c == PersonalTodoCapabilities.Complete => CompleteItem(),
                _ => throw new InvalidOperationException("Unexpected capability: " + capability)
            };
            return Task.FromResult(JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        private PersonalTodoItem CompleteItem() { CompletedItems++; return item; }
        public async IAsyncEnumerable<JsonElement> InvokeStreamingAsync(string capability, JsonElement arguments,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Assert.Equal(PlatformCapabilities.LlmChatStream, capability);
            Assert.Equal(workId, InferenceExecutionScope.Current?.Lease.WorkId);
            InferenceCalls++;
            await Task.Yield();
            yield return JsonSerializer.SerializeToElement(new { text = "Implemented" });
        }
        public Task ReportProgressAsync(AgentWorkLease lease, long sequence, JsonElement value, CancellationToken ct)
        { Assert.Equal(workId, lease.WorkId); ProgressSequences.Add(sequence); return Task.CompletedTask; }
        public Task RenewWorkAsync(AgentWorkLease lease, CancellationToken ct) => Task.CompletedTask;
        public Task CompleteAsync(AgentWorkLease lease, AgentWorkResult result, CancellationToken ct)
        { Finished.TrySetResult(); return Task.CompletedTask; }
        public Task FailAsync(AgentWorkLease lease, string error, CancellationToken ct)
        { Failures.Add(error); Finished.TrySetResult(); return Task.CompletedTask; }
        public Task CompleteRuntimeAsync(AgentWorkResult result, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<AgentToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentToolDescriptor>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
