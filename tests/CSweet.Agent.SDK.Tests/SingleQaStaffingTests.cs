using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class SingleQaStaffingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OneQaAuthorsReportsWhileManagerReviewsEvidenceAndQaReviewsProduct(bool software)
    {
        var f = new Fixture(software);
        var plan = await f.Prepare();
        Assert.Equal("Active", plan.Status);
        var report = f.Finalizations.Single(x => x.ItemId == f.Report.Id);
        Assert.Equal(f.Qa.AgentInstallationId, report.StageAssignments.Single(x => x.StageKey == f.AuthorStage).AgentInstallationId);
        Assert.Equal(f.Manager.AgentInstallationId, report.StageAssignments.Single(x => x.StageKey == "quality").AgentInstallationId);
        var product = f.Finalizations.Single(x => x.ItemId == f.Product.Id);
        Assert.Equal(f.Developer.AgentInstallationId, product.StageAssignments.Single(x => x.StageKey == f.AuthorStage).AgentInstallationId);
        Assert.Equal(f.Qa.AgentInstallationId, product.StageAssignments.Single(x => x.StageKey == "quality").AgentInstallationId);
        Assert.All(f.Configuration!.Assignments.Where(x => x.Scope is "Story" or "Release"), x =>
            Assert.Equal(f.Qa.AgentInstallationId, x.Stages.Single(s => s.StageKey == "quality").AgentInstallationId));
        Assert.All(f.Configuration.Assignments.Where(x => x.Scope is "Epic" or "Release"), x =>
            Assert.Equal(f.Technical.AgentInstallationId, x.Stages.Single(s => s.StageKey == "technical-review").AgentInstallationId));
        // The report gate is not self-review and the second pass does not redispatch work.
        Assert.NotEqual(report.StageAssignments[0].OrganizationUserId, report.StageAssignments[1].OrganizationUserId);
        await f.Prepare(); Assert.Equal(3, f.Finalizations.Count);
    }

    [Fact]
    public async Task MissingManagerReviewProducesActionableTaskGapBeforeAnyDeliveryIsConfigured()
    {
        var f = new Fixture(false); f.Members.Remove(f.Manager);
        var error = await Assert.ThrowsAsync<DeliveryStaffingException>(f.Prepare);
        Assert.Contains("QA report", error.Message); Assert.Contains("manager", error.Message);
        Assert.Null(f.Configuration); Assert.Empty(f.Finalizations);
    }

    [Fact]
    public async Task QaRoleDoesNotPermitSelfReviewOfCode()
    {
        var f = new Fixture(false);
        f.Items[f.Items.IndexOf(f.Report)] = f.Report with { Planning = f.Report.Planning! with { DeliveryKind = "Code" } };
        var error = await Assert.ThrowsAsync<DeliveryStaffingException>(f.Prepare);
        Assert.Contains("QA report", error.Message); Assert.Contains("game-quality-assurance", error.Message);
        Assert.Null(f.Configuration);
    }

    private sealed class Fixture
    {
        public readonly Guid Project = Guid.NewGuid(), BoardId = Guid.NewGuid(), EpicId = Guid.NewGuid(), StoryId = Guid.NewGuid();
        public readonly string AuthorStage;
        public readonly AgentTeammate Manager, Qa, Developer, Technical;
        public readonly WorkItem Product, Report;
        public readonly List<AgentTeammate> Members;
        public readonly List<WorkItem> Items = [];
        public readonly List<FinalizeWorkItemDeliveryRequest> Finalizations = [];
        public ConfigureWorkDeliveryPlanRequest? Configuration;
        private WorkDeliveryPlanResponse? plan;
        private readonly AgentRuntimeContext context;
        private readonly bool software;
        public Fixture(bool software)
        {
            this.software = software; AuthorStage = software ? "development" : "specialist-execution";
            Manager = Member("manager"); Qa = Member(software ? "software-qa" : "game-quality-assurance");
            Developer = Member(software ? "software-developer" : "game-engineer");
            Technical = Member(software ? "software-architect" : "game-technical-director");
            Members = [Manager, Qa, Developer, Technical];
            var epic = Item(EpicId, null, "Epic", "Release", null);
            var story = Item(StoryId, EpicId, "Story", "Story", null);
            Product = Item(Guid.NewGuid(), StoryId, "Task", "Product specification", Developer.DeclaredRoleKeys[0]);
            Report = Item(Guid.NewGuid(), StoryId, "Task", "QA report", Qa.DeclaredRoleKeys[0]);
            var architecture = Item(Guid.NewGuid(), StoryId, "Task", "Architecture plan", Technical.DeclaredRoleKeys[0]);
            Items.AddRange([epic, story, Product, Report, architecture]);
            var runtime = new AgentTestRuntime()
                .RegisterCapability<ReadWorkDeliveryPlansRequest, IReadOnlyList<WorkDeliveryPlanResponse>>(WorkDeliveryCapabilities.Read,
                    (_, _) => Task.FromResult<IReadOnlyList<WorkDeliveryPlanResponse>>(plan is null ? [] : [plan]))
                .RegisterCapability<JsonElement, object>(WorkItemCapabilities.Read, (r, _) => Task.FromResult<object>(
                    r.TryGetProperty("itemId", out var id) && id.ValueKind == JsonValueKind.String
                        ? Items.Single(x => x.Id == id.GetGuid())
                        : new WorkBoardDetail(new(BoardId, "TEST", "Test", false, false, 1, []), [], Items)))
                .RegisterCapability<ConfigureWorkDeliveryPlanRequest, WorkDeliveryPlanResponse>(WorkDeliveryCapabilities.Configure, (r, _) =>
                {
                    Configuration = r; plan = new(Guid.NewGuid(), Project, "Release", Guid.Parse(Manager.EmployeeId), "Draft", 1, 1,
                        [EpicId], [], [], [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow); return Task.FromResult(plan);
                })
                .RegisterCapability<FinalizeWorkItemDeliveryRequest, WorkItem>(WorkItemCapabilities.FinalizeDelivery, (r, _) =>
                {
                    Finalizations.Add(r); var i = Items.FindIndex(x => x.Id == r.ItemId);
                    Items[i] = Items[i] with { Delivery = r.Delivery, StageAssignments = r.StageAssignments }; return Task.FromResult(Items[i]);
                })
                .RegisterCapability<ControlWorkDeliveryPlanRequest, WorkDeliveryPlanResponse>(WorkDeliveryCapabilities.Control, (_, _) =>
                { plan = plan! with { Status = "Active" }; return Task.FromResult(plan); });
            context = runtime.CreateContext();
        }
        public Task<WorkDeliveryPlanResponse> Prepare() => HierarchicalProjectDelivery.PrepareAsync(Project, BoardId,
            Guid.Parse(Manager.EmployeeId), new(Guid.NewGuid().ToString(), "team", "Team", 1, Manager.EmployeeId, "Manager", Members, [], Members.Count, false),
            Guid.Empty, "", new string('a', 64), software, context, default);
        private WorkItem Item(Guid id, Guid? parent, string kind, string title, string? role) =>
            new(id, BoardId, parent, null, kind, title, "", "Ready", "High", null, 0, 1, null)
            { ExecutionMode = kind == "Task" ? WorkItemExecutionModes.Executable : WorkItemExecutionModes.Container,
              Planning = new(["Requirement"], ["Verified"]) { DeliveryKind = "Artifact", DelegationRecommendations = role is null ? [] :
                  [new(AuthorStage, role, [], null, true, "Role-owned work")] } };
        private static AgentTeammate Member(string role) => new(Guid.NewGuid().ToString(), role, "Agent", role, role, "Teammate", "Active")
            { AgentInstallationId = Guid.NewGuid(), RuntimeEligibility = "Eligible", IsAvailable = true,
              DeclaredRoleKeys = [role], EffectiveCapabilities = [WorkManagementCapabilityNames.ExecutionRunV2] };
    }
}
