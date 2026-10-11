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
        var proposal = Assert.Single(f.Configuration!.TaskAssignments, x => x.ItemId == f.Report.Id);
        Assert.Equal(JsonSerializer.Serialize(report.StageAssignments), JsonSerializer.Serialize(proposal.Stages));
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

    [Theory]
    [InlineData("work.delivery.configure.v1", true)]
    [InlineData("work.delivery.control.v1", true)]
    [InlineData("work.item.delivery.finalize", true)]
    [InlineData("platform.llm.chat", false)]
    public void OnlyDeliveryPlanningConflictsBecomeActionableStaffingWaits(string capability, bool handled)
    {
        var error = new PlatformCapabilityException(capability, PlatformCapabilityErrorCode.Unavailable,
            "{\"code\":\"Conflict\",\"message\":\"QA report requires manager review\"}");
        Assert.Equal(handled ? "QA report requires manager review" : null, HierarchicalProjectDelivery.DeliveryConflictReason(error));
        Assert.Null(HierarchicalProjectDelivery.DeliveryConflictReason(new(capability, PlatformCapabilityErrorCode.Denied, "No grant")));
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
    public async Task RejectedDraftRetainsActionableReasonAndCanResumeAfterThePlatformCorrection()
    {
        var f = new Fixture(false);
        f.ConfigureError = new(WorkDeliveryCapabilities.Configure, PlatformCapabilityErrorCode.Conflict,
            "QA report: finalize its independent manager review before activation");
        var error = await Assert.ThrowsAsync<DeliveryStaffingException>(f.Prepare);
        Assert.Contains("QA report", error.Message); Assert.Contains("manager review", error.Message);
        Assert.Empty(f.Finalizations);
        f.ConfigureError = null;
        Assert.Equal("Active", (await f.Prepare()).Status);
        var count = f.Finalizations.Count;
        await f.Prepare();
        Assert.Equal(count, f.Finalizations.Count);
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

    [Fact]
    public async Task GameSprintCanActivateWithComputeTestAndNoCertifiedCatalog()
    {
        var f = new Fixture(false, true);
        await f.Prepare();
        var release = f.Configuration!.Branches.Single(x => x.Scope == "Release");
        Assert.Equal("compute-test", release.ReadinessMode); Assert.Null(release.Build);
        Assert.Equal(f.Technical.AgentInstallationId, f.Configuration.Assignments.Single(x => x.Scope == "Release")
            .Stages.Single(x => x.StageKey == "build-readiness").AgentInstallationId);
        Assert.Contains(f.Configuration.Assignments.Single(x => x.Scope == "Release").Stages, x => x.StageKey == "quality");
        Assert.Contains(f.Configuration.Assignments.Single(x => x.Scope == "Release").Stages, x => x.StageKey == "technical-review");
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
        public PlatformCapabilityException? ConfigureError;
        private WorkDeliveryPlanResponse? plan;
        private readonly AgentRuntimeContext context;
        private readonly bool software;
        private readonly bool computeTest;
        public Fixture(bool software, bool computeTest = false)
        {
            this.software = software; this.computeTest = computeTest; AuthorStage = software ? "development" : "specialist-execution";
            Manager = Member("manager"); Qa = Member(software ? "software-qa" : "game-quality-assurance");
            Developer = Member(software ? "software-developer" : "game-engineer");
            Technical = Member(software ? "software-architect" : "game-technical-director");
            if (computeTest) Technical = Technical with { EffectiveCapabilities =
                [WorkManagementCapabilityNames.ExecutionRunV2, DeliveryBuildReadiness.ExecutionCapability, DeliveryComputeTest.ExecutionCapability] };
            Members = [Manager, Qa, Developer, Technical];
            var epic = Item(EpicId, null, "Epic", "Release", null);
            var story = Item(StoryId, EpicId, "Story", "Story", null);
            Product = Item(Guid.NewGuid(), StoryId, "Task", "Product specification", Developer.DeclaredRoleKeys[0]);
            Report = Item(Guid.NewGuid(), StoryId, "Task", "QA report", Qa.DeclaredRoleKeys[0]);
            var architecture = Item(Guid.NewGuid(), StoryId, "Task", "Architecture plan", Technical.DeclaredRoleKeys[0]);
            Items.AddRange([epic, story, Product, Report, architecture]);
            if (computeTest) Items[Items.IndexOf(Product)] = Product with { Planning = Product.Planning! with { DeliveryKind = "Code" } };
            var runtime = new AgentTestRuntime()
                .RegisterCapability<ReadWorkDeliveryPlansRequest, IReadOnlyList<WorkDeliveryPlanResponse>>(WorkDeliveryCapabilities.Read,
                    (_, _) => Task.FromResult<IReadOnlyList<WorkDeliveryPlanResponse>>(plan is null ? [] : [plan]))
                .RegisterCapability<JsonElement, object>(WorkItemCapabilities.Read, (r, _) => Task.FromResult<object>(
                    r.TryGetProperty("itemId", out var id) && id.ValueKind == JsonValueKind.String
                        ? Items.Single(x => x.Id == id.GetGuid())
                        : new WorkBoardDetail(new(BoardId, "TEST", "Test", false, false, 1, []), [], Items)))
                .RegisterCapability<ConfigureWorkDeliveryPlanRequest, WorkDeliveryPlanResponse>(WorkDeliveryCapabilities.Configure, (r, _) =>
                {
                    if (ConfigureError is not null) throw ConfigureError;
                    Configuration = r; plan = new(Guid.NewGuid(), Project, "Release", Guid.Parse(Manager.EmployeeId), "Draft", 1, 1,
                        [EpicId], r.Branches, [], [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow); return Task.FromResult(plan);
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
            computeTest ? Project : Guid.Empty, computeTest ? "main" : "", new string('a', 64), software, context, default);
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
