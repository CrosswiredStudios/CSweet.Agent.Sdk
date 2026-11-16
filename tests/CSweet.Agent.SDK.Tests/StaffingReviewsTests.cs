using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class StaffingReviewsTests
{
    [Fact]
    public void Legacy_wait_signature_and_new_exact_dependency_are_both_available()
    {
        Assert.NotNull(typeof(PersonalTodoResult).GetMethod("WaitingUntil", [typeof(DateTimeOffset), typeof(string), typeof(Guid?)]));
        var dependency = new PersonalTodoDependency(PersonalTodoDependencyKinds.ResourceChange, Guid.NewGuid(), "Pending");
        Assert.Null(PersonalTodoResult.WaitingUntil(DateTimeOffset.UtcNow.AddHours(1), "Legacy").Dependency);
        Assert.Equal(dependency, PersonalTodoResult.WaitingUntil(DateTimeOffset.UtcNow.AddHours(1), "Current", null, dependency).Dependency);
    }

    [Fact]
    public async Task Typed_defer_forwards_the_exact_reference_through_the_public_wire_command()
    {
        var dependency = new PersonalTodoDependency(PersonalTodoDependencyKinds.ResourceChange, Guid.NewGuid(), "Pending");
        var item = Guid.NewGuid(); var claim = Guid.NewGuid();
        var runtime = new AgentTestRuntime().RegisterCapability<DeferPersonalTodoItemRequest, PersonalTodoItem>(PersonalTodoCapabilities.Defer,
            (request, _) => {
                Assert.Equal(dependency, request.Dependency); Assert.Equal(item, request.ItemId); Assert.Equal(claim, request.EventId);
                Assert.Equal(7, request.ExpectedRevision);
                return Task.FromResult(JsonSerializer.Deserialize<PersonalTodoItem>("{}")!);
            });
        await runtime.CreateContext().Platform.PersonalTodo.DeferAsync(item, claim, 7, DateTimeOffset.UtcNow.AddHours(1),
            "Review pending", null, default, dependency);
    }

    [Fact]
    public void Named_relationship_and_full_work_survive_for_a_third_party_role()
    {
        var requester = Guid.NewGuid(); var manager = Guid.NewGuid();
        var role = new ResourceChangeRole("external-lab-specialist", "Research", "Lab specialist", "", 1, 1, "",
            ["vendor.lab.run.v1"], false, requester, null);
        var purpose = new string('x', 12000) + " Critical final responsibility.";
        var described = new StaffingWorkDescription(purpose, "After the first sample is available; until the investigation closes",
            "Manager", "Independent findings; escalate conflicts to the accountable manager", "Bounded investigation")
            .Apply(role, requester, manager);
        Assert.StartsWith(purpose, described.Purpose);
        Assert.Contains("Independent findings", described.Purpose);
        Assert.Equal(manager, described.ReportsToOrganizationUserId);
        Assert.Equal(role.RequiredCapabilities, described.RequiredCapabilities);
        Assert.Throws<ArgumentException>(() => new StaffingWorkDescription(purpose, "Now", manager.ToString(), "Own results", "Bounded")
            .Apply(role, requester, manager));
    }

    [Fact]
    public void Repeated_reviews_follow_latest_current_scope_not_event_order_or_one_retry_limit()
    {
        var owner=Guid.NewGuid(); var manager=Guid.NewGuid(); var team=Guid.NewGuid(); var project=Guid.NewGuid();
        var history = new List<ResourceChangeRequestResponse>();
        for (var i=0; i<5; i++)
        {
            var next = JsonSerializer.Deserialize<ResourceChangeRequestResponse>("{}")! with {
                Id=Guid.NewGuid(), RequesterOrganizationUserId=owner, ManagerOrganizationUserId=manager,
                TeamId=team, WorkstreamId=project, CreatedAt=DateTimeOffset.UtcNow.AddMinutes(i), Status="RevisionRequested" };
            history.Add(next);
            history.Add(next with { Id=Guid.NewGuid(), WorkstreamId=Guid.NewGuid(), CreatedAt=next.CreatedAt.AddDays(1) });
            Assert.Equal(next.Id, StaffingReviews.Latest(history.AsEnumerable().Reverse(),owner,manager,team,project)!.Id);
            Assert.True(StaffingReviews.NeedsRevision(next));
            Assert.Equal(new PersonalTodoDependency(PersonalTodoDependencyKinds.ResourceChange,next.Id,"RevisionRequested"),
                StaffingReviews.Dependency(next));
        }
        Assert.Null(StaffingReviews.Latest(history,Guid.NewGuid(),manager,team,project));
        Assert.Null(StaffingReviews.Latest(history,owner,Guid.NewGuid(),team,project));
    }
}
