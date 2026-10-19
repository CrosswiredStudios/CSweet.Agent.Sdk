using CSweet.WorkManagement.Contracts;
using System.Text.Json;

namespace CSweet.Agent.SDK.Tests;

public sealed class TicketWorkflowKeyTests
{
    [Theory]
    [InlineData(20)]
    [InlineData(110)]
    [InlineData(111)]
    [InlineData(150)]
    [InlineData(200)]
    [InlineData(400)]
    public void Derived_keys_are_bounded_stable_and_distinguish_scope_and_item(int length)
    {
        var plan = new string('p', length); var item = Guid.NewGuid();
        var key = TicketWorkflowPlanning.TicketKey(plan, "Story", item);
        Assert.InRange(key.Length, 1, 160);
        Assert.Equal(key, TicketWorkflowPlanning.TicketKey(plan, "Story", item));
        Assert.NotEqual(key, TicketWorkflowPlanning.TicketKey(plan, "Epic", item));
        Assert.NotEqual(key, TicketWorkflowPlanning.TicketKey(plan, "Story", Guid.NewGuid()));
        Assert.NotEqual(key, TicketWorkflowPlanning.TicketKey(plan + "x", "Story", item));
        var original = plan + ":ticket:Story:" + item;
        if (original.Length <= 160) Assert.Equal(original, key);
        else Assert.StartsWith("workflow-ticket:v1:", key);
    }

    [Fact]
    public async Task Publication_replays_existing_workflow_tickets_after_configuration_interruption()
    {
        var epic = JsonSerializer.Deserialize<WorkItem>("{}")! with { Id = Guid.NewGuid(), Planning = new(["Build prototype"], ["Prototype works"]) };
        var board = Guid.NewGuid(); var manager = Guid.NewGuid();
        var request = JsonSerializer.Deserialize<ConfigureWorkDeliveryPlanRequest>("{}")! with {
            WorkstreamId = Guid.NewGuid(), Name = "Prototype", ManagerOrganizationUserId = manager,
            EpicItemIds = [epic.Id], Branches = [], IdempotencyKey = new string('p', 190),
            Assignments = [new("Release", null, board, [])]
        };
        var created = new Dictionary<string, WorkItem>(); var configurationCalls = 0;
        var runtime = new AgentTestRuntime()
            .RegisterCapability<CreateWorkItemRequest, WorkItem>(WorkItemCapabilities.Create, (r, _) => {
                Assert.InRange(r.IdempotencyKey.Length, 1, 160);
                if (!created.TryGetValue(r.IdempotencyKey, out var ticket))
                    created[r.IdempotencyKey] = ticket = epic with { Id = Guid.NewGuid(), Title = r.Title, Planning = r.Planning, TypeKey = r.TypeKey! };
                return Task.FromResult(ticket);
            })
            .RegisterCapability<ConfigureWorkDeliveryPlanRequest, WorkDeliveryPlanResponse>(WorkDeliveryCapabilities.Configure, (r, _) => {
                if (++configurationCalls == 1) throw new InvalidOperationException("Interrupted after publication");
                Assert.Equal(Assert.Single(created.Values).Id, Assert.Single(r.TicketWorkflows!).TicketId);
                return Task.FromResult(JsonSerializer.Deserialize<WorkDeliveryPlanResponse>("{}")! with { Status = "Draft" });
            });
        await Assert.ThrowsAsync<InvalidOperationException>(() => TicketWorkflowPlanning.ConfigureAsync(request, [epic], runtime.CreateContext(), default));
        await TicketWorkflowPlanning.ConfigureAsync(request, [epic], runtime.CreateContext(), default);
        Assert.Single(created); Assert.Equal(2, configurationCalls);
    }
}
