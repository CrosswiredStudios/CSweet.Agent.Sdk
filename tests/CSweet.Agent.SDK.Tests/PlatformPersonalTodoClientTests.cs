using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class PlatformPersonalTodoClientTests
{
    [Fact]
    public async Task AddAsync_UsesTypedGrantGovernedCapability()
    {
        AddPersonalTodoItemRequest? captured = null;
        var ownerId = Guid.NewGuid();
        var item = new PersonalTodoItem(
            Guid.NewGuid(), Guid.NewGuid(), ownerId, ownerId, "Owner", "Task", "",
            PersonalTodoStatuses.Ready, WorkPriorities.Medium, 1024, 1, null, null, null, [], null,
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var runtime = new AgentTestRuntime().RegisterCapability<
            AddPersonalTodoItemRequest, PersonalTodoItem>(
            PersonalTodoCapabilities.Add,
            (request, _) => { captured = request; return Task.FromResult(item); });
        var request = new AddPersonalTodoItemRequest(
            "Task", null, WorkPriorities.Medium, null, "todo-1");

        var result = await runtime.CreateContext().Platform.PersonalTodo.AddAsync(request);

        Assert.Equal(request, captured);
        Assert.Equal(item.Id, result.Id);
    }

    [Fact]
    public async Task BaseAgent_BlocksUnsupportedPersonalWork()
    {
        var ownerId = Guid.NewGuid();
        var item = new PersonalTodoItem(
            Guid.NewGuid(), Guid.NewGuid(), ownerId, ownerId, "Owner", "Task", "",
            PersonalTodoStatuses.Ready, WorkPriorities.Medium, 1024, 1, null, null, null, [], null,
            null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var result = await new UnsupportedAgent().HandlePersonalTodoAsync(
            item, new AgentTestRuntime().CreateContext(), CancellationToken.None);

        Assert.NotNull(result);
    }

    private sealed class UnsupportedAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.unsupported";
        public override string Version => "1.0.0";
    }
}
