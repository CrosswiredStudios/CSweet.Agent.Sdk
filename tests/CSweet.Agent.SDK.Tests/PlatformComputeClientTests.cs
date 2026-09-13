using System.Text.Json;
using CSweet.Agent.SDK.Compute;

namespace CSweet.Agent.SDK.Tests;

public sealed class PlatformComputeClientTests
{
    [Theory]
    [InlineData("linux", "x64")]
    [InlineData("windows", "arm64")]
    public async Task Provision_preserves_requirements_keys_and_runtime_owned_scope(string os, string architecture)
    {
        JsonElement captured = default;
        var runtime = new AgentTestRuntime().RegisterCapability<JsonElement, ComputeEnvironment>(ComputeCapabilities.Provision,
            (input, _) => { captured = input; return Task.FromResult(Environment()); });
        var request = new ProvisionComputeRequest(Guid.NewGuid(), "test-app", "stable-create", new(os, architecture, "clean", new(1, 1024, 20480), 3600));
        await runtime.CreateContext().Platform.Compute.ProvisionAsync(request);
        Assert.Equal("stable-create", captured.GetProperty("idempotencyKey").GetString());
        Assert.Equal(os, captured.GetProperty("specification").GetProperty("operatingSystem").GetString());
        Assert.Equal(architecture, captured.GetProperty("specification").GetProperty("architecture").GetString());
        Assert.Equal("ephemeral", captured.GetProperty("specification").GetProperty("persistence").GetString());
        Assert.False(captured.GetProperty("specification").TryGetProperty("network", out _));
        Assert.False(captured.TryGetProperty("installationId", out _));
        Assert.False(captured.TryGetProperty("organizationId", out _));
    }

    [Fact]
    public async Task Command_and_port_use_distinct_capabilities_and_closed_workloads()
    {
        var id = Guid.NewGuid(); var commandId = Guid.NewGuid();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<JsonElement, ComputeOperation>(ComputeCapabilities.Execute, (input, _) => {
                Assert.Equal(id, input.GetProperty("environmentId").GetGuid());
                Assert.Equal(3, input.GetProperty("expectedGeneration").GetInt64());
                var workload = input.GetProperty("workload");
                Assert.Single(workload.EnumerateObject());
                Assert.Equal(commandId, workload.GetProperty("command").GetProperty("requestId").GetGuid());
                Assert.Equal("literal; $argument", workload.GetProperty("command").GetProperty("arguments")[0].GetString());
                return Task.FromResult(Operation());
            })
            .RegisterCapability<JsonElement, ComputeOperation>(ComputeCapabilities.PublishPort, (input, _) => {
                var workload = input.GetProperty("workload");
                Assert.Single(workload.EnumerateObject()); Assert.Equal(8080, workload.GetProperty("publishPort").GetInt32());
                Assert.Equal("stable-port", input.GetProperty("idempotencyKey").GetString());
                return Task.FromResult(Operation());
            });
        var client = runtime.CreateContext().Platform.Compute;
        await client.ExecuteAsync(new(id, 3, "stable-command", new(commandId, "/bin/echo", "/work", ["literal; $argument"])));
        await client.PublishPortAsync(new(id, 4, "stable-port", 8080));
    }

    [Theory]
    [InlineData(ComputeCapabilities.Start)]
    [InlineData(ComputeCapabilities.Stop)]
    [InlineData(ComputeCapabilities.Restart)]
    [InlineData(ComputeCapabilities.Destroy)]
    public async Task Lifecycle_uses_exact_action_generation_and_key(string action)
    {
        var request = new ComputeLifecycleRequest(Guid.NewGuid(), 7, "stable-lifecycle");
        var runtime = new AgentTestRuntime().RegisterCapability<JsonElement, ComputeEnvironment>(action, (input, _) => {
            Assert.Equal(3, input.EnumerateObject().Count());
            Assert.Equal(request.EnvironmentId, input.GetProperty("environmentId").GetGuid());
            Assert.Equal(7, input.GetProperty("expectedGeneration").GetInt64());
            Assert.Equal(request.IdempotencyKey, input.GetProperty("idempotencyKey").GetString());
            return Task.FromResult(Environment());
        });
        var client = runtime.CreateContext().Platform.Compute;
        await (action switch {
            ComputeCapabilities.Start => client.StartAsync(request), ComputeCapabilities.Stop => client.StopAsync(request),
            ComputeCapabilities.Restart => client.RestartAsync(request), _ => client.DestroyAsync(request)
        });
    }

    [Fact]
    public async Task Reads_and_pagination_preserve_selectors_and_deserialize_guest_bytes_without_transport_details()
    {
        var environmentId = Guid.NewGuid(); var operationId = Guid.NewGuid(); var cursor = Guid.NewGuid();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<JsonElement, object>(ComputeCapabilities.Read, (input, _) => {
                Assert.Single(input.EnumerateObject());
                if (input.TryGetProperty("environmentId", out var id)) { Assert.Equal(environmentId, id.GetGuid()); return Task.FromResult<object>(Environment()); }
                Assert.Equal(operationId, input.GetProperty("operationId").GetGuid());
                return Task.FromResult<object>(new { id = operationId, environmentId, generation = 2, status = "Completed",
                    result = new { command = new { version = 1, challenge = Guid.NewGuid(), requestId = Guid.NewGuid(), exitCode = 0,
                        timedOut = false, standardOutput = Convert.ToBase64String("hello"u8), standardError = "", truncated = true } } });
            })
            .RegisterCapability<JsonElement, ComputeEnvironmentPage>(ComputeCapabilities.List, (input, _) => {
                Assert.Equal(cursor, input.GetProperty("afterId").GetGuid()); Assert.Equal(10, input.GetProperty("limit").GetInt32());
                return Task.FromResult(new ComputeEnvironmentPage([Environment()], cursor));
            });
        var client = runtime.CreateContext().Platform.Compute;
        await client.ReadAsync(environmentId);
        var result = await client.ReadOperationAsync(operationId);
        Assert.Equal("hello", result.Result!.Command!.StandardOutputText); Assert.True(result.Result.Command.Truncated);
        Assert.Equal(cursor, (await client.ListAsync(Guid.NewGuid(), cursor, 10)).NextAfterId);
    }

    [Fact]
    public async Task Cancellation_denial_and_unknown_results_do_not_resubmit_commands()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var count = 0;
        var runtime = new AgentTestRuntime().RegisterCapability<JsonElement, ComputeOperation>(ComputeCapabilities.Execute, (_, token) => {
            count++; Assert.Equal(cancellation.Token, token); token.ThrowIfCancellationRequested(); return Task.FromResult(Operation());
        });
        var request = new ExecuteComputeCommandRequest(Guid.NewGuid(), 1, "one-command", new(Guid.NewGuid(), "/bin/true", "/", []));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.CreateContext().Platform.Compute.ExecuteAsync(request, cancellation.Token));
        Assert.Equal(1, count);
        await Assert.ThrowsAsync<PlatformCapabilityException>(() => new AgentTestRuntime().CreateContext().Platform.Compute.ExecuteAsync(request));
        runtime.RegisterCapability<JsonElement, ComputeOperation>(ComputeCapabilities.Execute, (_, _) => {
            count++; return Task.FromResult(Operation() with { Status = "Completed", Result = new(ErrorCode: "outcome-unknown") });
        });
        Assert.Equal("outcome-unknown", (await runtime.CreateContext().Platform.Compute.ExecuteAsync(request)).Result!.ErrorCode);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Invalid_inputs_fail_before_transport()
    {
        var client = new AgentTestRuntime().CreateContext().Platform.Compute;
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadAsync(Guid.Empty));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.ListAsync(Guid.NewGuid(), limit: 101));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.PublishPortAsync(new(Guid.NewGuid(), 1, "key", 80)));
        await Assert.ThrowsAsync<ArgumentException>(() => client.StopAsync(new(Guid.NewGuid(), 1, "")));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ExecuteAsync(new(Guid.NewGuid(), 1, "key", new(Guid.NewGuid(), "/bin/true", "/", [], 31))));
        await Assert.ThrowsAsync<ArgumentException>(() => client.ProvisionAsync(new(Guid.NewGuid(), "desired", "key",
            new("linux", "x64", "clean", new(1, 1024, 20480), 60, Network: new("none", AllowOutbound: true)))));
    }

    [Fact]
    public void Change_event_contains_only_a_hint_for_authorized_current_state_reads()
    {
        var id = Guid.NewGuid();
        var change = JsonSerializer.Deserialize<ComputeChangedEvent>($$"""{"environmentId":"{{id}}","revision":9}""", new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(id, change!.EnvironmentId); Assert.Equal(9, change.Revision);
        Assert.Equal("com.csweet.compute.changed.v1", ComputeEvents.Changed);
    }

    private static ComputeEnvironment Environment() => new(Guid.NewGuid(), 1, 1, "running", "ready", "ephemeral", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), null);
    private static ComputeOperation Operation() => new(Guid.NewGuid(), Guid.NewGuid(), 1, "Pending", null, null);
}
