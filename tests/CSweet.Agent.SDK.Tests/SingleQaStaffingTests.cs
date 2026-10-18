using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class SingleQaStaffingTests
{
    [Fact]
    public async Task Selected_increment_excludes_empty_future_scope_and_retains_independent_acceptance()
    {
        var f = new Fixture(false);
        var future = f.Items.Single(x => x.Id == f.EpicId) with { Id = Guid.NewGuid(), Title = "Later agreed scope" };
        f.Items.Add(future);
        f.Items.Add(f.Items.Single(x => x.Id == f.StoryId) with { Id = Guid.NewGuid(), ParentItemId = future.Id });
        var plan = await f.PrepareIncrement([f.EpicId]);
        Assert.Equal("Active", plan.Status);
        Assert.Equal(f.EpicId, Assert.Single(plan.EpicItemIds));
        Assert.DoesNotContain(f.Configuration!.Assignments, x => x.ItemId == future.Id);
        Assert.All(f.Configuration.Assignments.Where(x => x.Scope is "Story" or "Release"), x =>
            Assert.Equal(f.Qa.AgentInstallationId, x.Stages.Single(s => s.StageKey == "quality").AgentInstallationId));
        await Assert.ThrowsAsync<DeliveryStaffingException>(() => f.PrepareIncrement([future.Id]));
        Assert.Equal(1, f.ConfigurationCount);
    }

    [Fact]
    public async Task Selected_increment_rejects_empty_foreign_or_undetailed_scope_before_mutation()
    {
        var f = new Fixture(false);
        await Assert.ThrowsAsync<ArgumentException>(() => f.PrepareIncrement([]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.PrepareIncrement([Guid.NewGuid()]));
        f.Items.RemoveAll(x => x.ParentItemId == f.StoryId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.PrepareIncrement([f.EpicId]));
        Assert.Null(f.Configuration); Assert.Empty(f.Finalizations);
    }

    [Fact]
    public async Task CreativeDirectorWithOnlyCanonicalExecutionCanAuthorWithoutDuplicateHiring()
    {
        var f = new Fixture(false);
        var director = f.Developer with { DeclaredRoleKeys = ["creative-director"],
            EffectiveCapabilities = [WorkManagementCapabilityNames.ExecutionRun] };
        f.Members[f.Members.IndexOf(f.Developer)] = director;
        f.Items[f.Items.IndexOf(f.Product)] = f.Product with { Planning = f.Product.Planning! with
        {
            DelegationRecommendations = [new(f.AuthorStage, "creative-director",
                [WorkManagementCapabilityNames.ExecutionRun], null, true, "Level grid specification")]
        } };
        Assert.Equal("Active", (await f.Prepare()).Status);
        var stages = f.Finalizations.Single(x => x.ItemId == f.Product.Id).StageAssignments;
        var author = stages.Single(x => x.StageKey == f.AuthorStage);
        Assert.Equal(director.AgentInstallationId, author.AgentInstallationId);
        Assert.Equal(WorkManagementCapabilityNames.ExecutionRun, Assert.Single(author.Requirements!.RequiredCapabilityKeys));
        Assert.NotEqual(author.AgentInstallationId, stages.Single(x => x.StageKey == "quality").AgentInstallationId);
    }

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

    [Fact]
    public async Task DraftRecoveryRefreshesOnlyChangedPlanningAndRetainsIdentity()
    {
        var f = new Fixture(false) { ControlError = new(WorkDeliveryCapabilities.Control, PlatformCapabilityErrorCode.Conflict, "preflight unavailable") };
        await Assert.ThrowsAsync<DeliveryStaffingException>(f.Prepare);
        var draft = f.plan!;
        Assert.Equal(3, f.Finalizations.Count);
        await Assert.ThrowsAsync<DeliveryStaffingException>(f.Prepare);
        Assert.Equal(1, f.ConfigurationCount);
        Assert.Equal(3, f.Finalizations.Count);
        var index = f.Items.FindIndex(x => x.Id == f.Product.Id);
        var task = f.Items[index];
        f.Items[index] = task with { PlanningRevision = task.PlanningRevision + 1, Revision = task.Revision + 1,
            Planning = task.Planning! with { AcceptanceCriteria = ["Updated criterion"] } };
        f.ControlError = null;
        var active = await f.Prepare();
        Assert.Equal(draft.Id, active.Id);
        Assert.Equal(draft.Id, f.Configuration!.PlanId);
        Assert.Equal(draft.Revision, f.Configuration.ExpectedRevision);
        Assert.Equal(2, f.ConfigurationCount);
        Assert.Equal(4, f.Finalizations.Count);
        Assert.Equal("Updated criterion", Assert.Single(f.Finalizations.Last().Delivery.AcceptanceCriteria));
        await f.Prepare();
        Assert.Equal(4, f.Finalizations.Count);
    }

    [Fact]
    public async Task DraftRetryIgnoresDatabaseAssignmentOrderAndSelectionTimestamps()
    {
        var f = new Fixture(false, true) { ControlError = new(WorkDeliveryCapabilities.Control, PlatformCapabilityErrorCode.Conflict, "GitHost unavailable") };
        await Assert.ThrowsAsync<DeliveryStaffingException>(f.Prepare);
        for (var i = 0; i < f.Items.Count; i++)
            f.Items[i] = f.Items[i] with { StageAssignments = f.Items[i].StageAssignments.Reverse()
                .Select(stage => stage with { SelectionEvidence = stage.SelectionEvidence is { } evidence
                    ? evidence with { SelectedAt = evidence.SelectedAt.AddSeconds(1) } : null }).ToArray() };
        f.plan = f.plan! with { Scopes = f.plan.Scopes.Select(scope => scope with { Stages = scope.Stages.Reverse().ToArray() }).ToArray() };
        await Assert.ThrowsAsync<DeliveryStaffingException>(f.Prepare);
        Assert.Equal(1, f.ConfigurationCount);
        Assert.Equal(3, f.Finalizations.Count);
        f.ControlError = null;
        Assert.Equal("Active", (await f.Prepare()).Status);
        Assert.Equal(3, f.Finalizations.Count);
    }

    [Fact]
    public async Task InterruptedTaskFinalizationResumesWithoutReplacingCompletedSteps()
    {
        var f = new Fixture(false) { FinalizeFailure = true };
        await Assert.ThrowsAsync<DeliveryStaffingException>(f.Prepare);
        Assert.Single(f.Finalizations); var draftId = f.plan!.Id;
        f.FinalizeFailure = false;
        Assert.Equal(draftId, (await f.Prepare()).Id);
        Assert.Equal(1, f.ConfigurationCount); Assert.Equal(3, f.Finalizations.Count);
        Assert.Equal(3, f.Finalizations.Select(x => x.ItemId).Distinct().Count());
    }

    [Fact]
    public async Task AddedChildRefreshesDraftScopeAndRetainsExplicitBranchBindings()
    {
        var f = new Fixture(false, true) { ControlError = new(WorkDeliveryCapabilities.Control, PlatformCapabilityErrorCode.Conflict, "preflight unavailable") };
        await Assert.ThrowsAsync<DeliveryStaffingException>(f.Prepare);
        var original = f.plan!;
        var release = original.Branches.Single(x => x.Scope == "Release") with { SourceBranch = "csweet/release/custom" };
        var story = original.Branches.Single(x => x.Scope == "Story") with { TargetBranch = release.SourceBranch };
        f.plan = original with { Branches = [story, release] };
        f.Items.Add(f.Items.Single(x => x.Id == f.Report.Id) with { Id = Guid.NewGuid(), Delivery = null, StageAssignments = [], Title = "Additional report" });
        f.ControlError = null;
        var active = await f.Prepare();
        Assert.Equal(original.Id, active.Id); Assert.Equal(2, f.ConfigurationCount);
        Assert.Equal(release, f.Configuration!.Branches.Single(x => x.Scope == "Release"));
        Assert.Equal(story, f.Configuration.Branches.Single(x => x.Scope == "Story"));
        Assert.Equal(4, active.Scopes.Single(x => x.Scope == "Story").ChildIds.Count);
        Assert.Equal(4, f.Finalizations.Count);
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
        public WorkDeliveryPlanResponse? plan;
        public PlatformCapabilityException? ControlError;
        public int ConfigurationCount;
        public bool FinalizeFailure;
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
                [WorkManagementCapabilityNames.ExecutionRun, DeliveryBuildReadiness.ExecutionCapability, DeliveryComputeTest.ExecutionCapability] };
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
                    if (r.PlanId.HasValue) { Assert.Equal(plan!.Id, r.PlanId); Assert.Equal(plan.Revision, r.ExpectedRevision); }
                    Configuration = r; ConfigurationCount++;
                    var snapshots = r.Assignments.Select(a => {
                        var parent = Items.SingleOrDefault(x => x.Id == a.ItemId);
                        var children = Items.Where(x => a.Scope == "Release" ? x.Id == EpicId : x.ParentItemId == a.ItemId).ToArray();
                        return new WorkDeliveryScopeSnapshot(a.Scope, a.ItemId, BoardId, parent?.PlanningRevision ?? 1,
                            children.Select(x => x.Id).ToArray(), parent?.Planning ?? new(["Requirement"], ["Verified"]), a.Stages)
                            { ChildPlanningRevisions = children.ToDictionary(x => x.Id, x => x.PlanningRevision) };
                    }).ToArray();
                    plan = new(r.PlanId ?? Guid.NewGuid(), Project, "Release", Guid.Parse(Manager.EmployeeId), "Draft", (plan?.Revision ?? 0) + 1, 1,
                        [EpicId], r.Branches, snapshots, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow); return Task.FromResult(plan);
                })
                .RegisterCapability<FinalizeWorkItemDeliveryRequest, WorkItem>(WorkItemCapabilities.FinalizeDelivery, (r, _) =>
                {
                    if (FinalizeFailure && Finalizations.Count == 1)
                        throw new PlatformCapabilityException(WorkManagementCapabilityNames.ItemFinalizeDelivery, PlatformCapabilityErrorCode.Conflict, "temporary finalization conflict");
                    Finalizations.Add(r); var i = Items.FindIndex(x => x.Id == r.ItemId);
                    Items[i] = Items[i] with { Delivery = r.Delivery, StageAssignments = r.StageAssignments }; return Task.FromResult(Items[i]);
                })
                .RegisterCapability<ControlWorkDeliveryPlanRequest, WorkDeliveryPlanResponse>(WorkDeliveryCapabilities.Control, (_, _) =>
                {
                    if (ControlError is not null) throw ControlError;
                    Assert.All(plan!.Scopes, scope => Assert.All(scope.ChildPlanningRevisions, child =>
                        Assert.Equal(child.Value, Items.Single(x => x.Id == child.Key).PlanningRevision)));
                    plan = plan with { Status = "Active" }; return Task.FromResult(plan);
                });
            context = runtime.CreateContext();
        }
        public Task<WorkDeliveryPlanResponse> Prepare() => HierarchicalProjectDelivery.PrepareAsync(Project, BoardId,
            Guid.Parse(Manager.EmployeeId), new(Guid.NewGuid().ToString(), "team", "Team", 1, Manager.EmployeeId, "Manager", Members, [], Members.Count, false),
            computeTest ? Project : Guid.Empty, computeTest ? "main" : "", new string('a', 64), software, context, default);
        public Task<WorkDeliveryPlanResponse> PrepareIncrement(IReadOnlyCollection<Guid> epics) => HierarchicalProjectDelivery.PrepareIncrementAsync(Project, BoardId,
            Guid.Parse(Manager.EmployeeId), new(Guid.NewGuid().ToString(), "team", "Team", 1, Manager.EmployeeId, "Manager", Members, [], Members.Count, false),
            Guid.Empty, "", new string('a', 64), software, epics, context, default);
        private WorkItem Item(Guid id, Guid? parent, string kind, string title, string? role) =>
            new(id, BoardId, parent, null, kind, title, "", "Ready", "High", null, 0, 1, null)
            { ExecutionMode = kind == "Task" ? WorkItemExecutionModes.Executable : WorkItemExecutionModes.Container,
              Planning = new(["Requirement"], ["Verified"]) { DeliveryKind = "Artifact", DelegationRecommendations = role is null ? [] :
                  [new(AuthorStage, role, [], null, true, "Role-owned work")] } };
        private static AgentTeammate Member(string role) => new(Guid.NewGuid().ToString(), role, "Agent", role, role, "Teammate", "Active")
            { AgentInstallationId = Guid.NewGuid(), RuntimeEligibility = "Eligible", IsAvailable = true,
              DeclaredRoleKeys = [role], EffectiveCapabilities = [WorkManagementCapabilityNames.ExecutionRun] };
    }
}
