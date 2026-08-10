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

        Assert.NotNull(captured);
        Assert.Equal(request.Title, captured.Title);
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

    [Fact]
    public async Task UpdateAsync_PreservesStructuredTicketMentions()
    {
        var ownerId = Guid.NewGuid();
        var mentionedId = Guid.NewGuid();
        UpdatePersonalTodoItemRequest? captured = null;
        var item = new PersonalTodoItem(
            Guid.NewGuid(), Guid.NewGuid(), ownerId, ownerId, "Owner", "Tell @Matt a joke", "",
            PersonalTodoStatuses.Ready, WorkPriorities.Medium, 1024, 2, null, null, null,
            [new PersonalTodoMention(mentionedId, "Matt", "Human")], null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var runtime = new AgentTestRuntime().RegisterCapability<
            UpdatePersonalTodoItemRequest, PersonalTodoItem>(
            PersonalTodoCapabilities.Update,
            (request, _) => { captured = request; return Task.FromResult(item); });
        var request = new UpdatePersonalTodoItemRequest(
            item.Id, item.Title, item.Description, item.Priority, null, item.Revision, "update-1",
            [new WorkItemMentionInput(mentionedId, WorkItemMentionFields.Title, 5, 5)]);

        await runtime.CreateContext().Platform.PersonalTodo.UpdateAsync(request);

        Assert.NotNull(captured);
        Assert.Equal(request.ItemId, captured.ItemId);
        Assert.Equal(request.Title, captured.Title);
        Assert.Equal(mentionedId, Assert.Single(captured!.Mentions!).OrganizationUserId);
    }

    private sealed class UnsupportedAgent : CSweetAgentBase
    {
        public override string AgentId => "com.example.unsupported";
        public override string Version => "1.0.0";
    }
}
