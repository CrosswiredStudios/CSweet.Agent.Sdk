using System.Text.Json;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK.Tests;

public sealed class DeliveryCorrectionCoordinatorTests
{
    [Fact]
    public async Task Lost_create_response_and_cold_restart_reuse_one_corrective_task_and_start_a_separate_sprint_without_a_model()
    {
        var f=new Fixture { LoseCreateResponse=true };
        await Assert.ThrowsAsync<IOException>(()=>DeliveryCorrectionCoordinator.ReconcileProjectAsync(f.Project,f.Context(),default));
        Assert.Single(f.Created.Where(x=>x.Key.EndsWith(":task")));
        await DeliveryCorrectionCoordinator.ReconcileProjectAsync(f.Project,f.Context(),default);
        Assert.Single(f.Created.Where(x=>x.Key.EndsWith(":task")));
        Assert.Equal("Completed",f.Source.Status);Assert.Equal(f.OriginalSprint,f.Source.SprintId);
        Assert.NotEqual(f.OriginalSprint,f.Sprint!.Id);Assert.Equal("Active",f.Sprint.Status);
        Assert.Equal(1,f.Starts);Assert.Equal("Paused",f.Plans[0].Status);
        var follow=Assert.Single(f.Plans.Where(p=>p.Id!=f.OriginalPlan));
        Assert.Equal("Active",follow.Status);
        Assert.Equal(new[]{"technical-review","quality","manager-review"},follow.Scopes.Single(s=>s.Scope=="Release").TicketWorkflow!.Steps.Select(s=>s.Key));
        Assert.Equal(f.Plans[0].Scopes.Single(s=>s.Scope=="Release").Planning.AcceptanceCriteria,
            follow.Scopes.Single(s=>s.Scope=="Release").Planning.AcceptanceCriteria);
        await DeliveryCorrectionCoordinator.ReconcileProjectAsync(f.Project,f.Context(),default);
        Assert.Equal(1,f.Starts);Assert.Equal(3,f.Created.Count);Assert.Equal("Open",f.Plans[0].Findings[0].Status);
    }

    [Fact]
    public async Task Interrupted_sprint_start_resumes_bound_work_instead_of_replanning_or_claiming_acceptance()
    {
        var f=new Fixture { DenyStart=true };
        await DeliveryCorrectionCoordinator.ReconcileProjectAsync(f.Project,f.Context(),default);
        Assert.Contains("preflight",Assert.Single(f.Plans[0].Corrections).WaitingReason);
        Assert.Equal(3,f.Created.Count);Assert.Equal(0,f.Starts);
        f.DenyStart=false;
        await DeliveryCorrectionCoordinator.ReconcileProjectAsync(f.Project,f.Context(),default);
        Assert.Equal(1,f.Starts);Assert.Equal(3,f.Created.Count);
    }

    [Fact]
    public async Task Multiple_implementation_owners_require_an_owned_selection_without_creating_speculative_work()
    {
        var f=new Fixture();f.Ambiguous=true;
        await DeliveryCorrectionCoordinator.ReconcileProjectAsync(f.Project,f.Context(),default);
        Assert.Empty(f.Created);Assert.Equal(0,f.Starts);
        var c=Assert.Single(f.Plans[0].Corrections);Assert.Equal(f.Manager,c.OwnerOrganizationUserId);
        Assert.Contains("Select the source task",c.WaitingReason);
    }

    [Fact]
    public async Task Two_source_prism_case_selects_scene_scope_before_creating_work_and_starts_corrective_sprint()
    {
        var f = PrismCase();
        using var client = new SelectionClient(f.ChooseScene());
        await DeliveryCorrectionCoordinator.ReconcileProjectAsync(f.Project, f.Context(), () => client, default);

        Assert.Equal(1, client.Calls);
        Assert.Contains("Out of scope: scene code", client.Input);
        Assert.Contains("SAMPLE_POINTS.brickExplosive", client.Input);
        var correction = Assert.Single(f.Plans[0].Corrections);
        Assert.Equal(f.Source.Id, correction.SourceItemId);
        Assert.NotNull(correction.FollowUpPlanId);
        Assert.Equal(3, f.Created.Count);
        Assert.Equal("Active", f.Sprint!.Status);
        Assert.Equal(1, f.Starts);
        Assert.Equal(new[] { "select", "create", "create", "create", "bind" }, f.Effects);
        Assert.Equal("Completed", f.Source.Status);
        Assert.Equal("Completed", f.OtherSource!.Status);
        Assert.Equal(f.OriginalSprint, f.Source.SprintId);
        Assert.Equal("Open", f.Plans[0].Findings[0].Status);

        await DeliveryCorrectionCoordinator.ReconcileProjectAsync(f.Project, f.Context(),
            () => throw new InvalidOperationException("Saved selection must not ask the model again"), default);
        Assert.Equal(3, f.Created.Count);
        Assert.Equal(1, f.Starts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Saved_multi_source_selection_survives_lost_selection_or_task_response(bool loseSelection)
    {
        var f = PrismCase();
        f.LoseSelectionResponse = loseSelection;
        f.LoseCreateResponse = !loseSelection;
        using var client = new SelectionClient(f.ChooseScene());
        await Assert.ThrowsAsync<IOException>(() => DeliveryCorrectionCoordinator.ReconcileProjectAsync(
            f.Project, f.Context(), () => client, default));
        Assert.Equal(f.Source.Id, f.Plans[0].Corrections[0].SourceItemId);
        await DeliveryCorrectionCoordinator.ReconcileProjectAsync(f.Project, f.Context(),
            () => throw new InvalidOperationException("Cold restart must reuse persisted source"), default);
        Assert.Equal(1, client.Calls);
        Assert.Equal(3, f.Created.Count);
        Assert.Equal(1, f.Starts);
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("fabricated-scope")]
    [InlineData("fabricated-finding")]
    [InlineData("shared-scope")]
    [InlineData("missing-finding")]
    [InlineData("duplicate-finding")]
    [InlineData("mixed-sources")]
    [InlineData("malformed")]
    [InlineData("uncertain")]
    [InlineData("null-choice")]
    public async Task Unsupported_or_ambiguous_selection_retains_manager_wait_without_mutations(string scenario)
    {
        var f = PrismCase();
        var finding = f.Plans[0].Findings[0];
        object Choice(Guid source, string scope = "Implement the placeholder scene and its sample mapping", string? findingEvidence = null) =>
            new { findingId = finding.Id, sourceItemId = source, scopeEvidence = scope, findingEvidence = findingEvidence ?? finding.Summary };
        var choices = scenario switch
        {
            "outside" => new[] { Choice(Guid.NewGuid()) },
            "fabricated-scope" => new[] { Choice(f.Source.Id, "A fabricated implementation scope") },
            "fabricated-finding" => new[] { Choice(f.Source.Id, findingEvidence: "A fabricated validation result") },
            "shared-scope" => new[] { Choice(f.Source.Id, "Preserve all accepted project constraints") },
            "missing-finding" => Array.Empty<object>(),
            "duplicate-finding" => new[] { Choice(f.Source.Id), Choice(f.Source.Id) },
            "mixed-sources" => new[] { Choice(f.Source.Id), Choice(f.OtherSource!.Id) },
            _ => new[] { Choice(f.Source.Id) }
        };
        var response = scenario == "malformed" ? "{" : scenario == "null-choice" ?
            "{\"disposition\":\"Select\",\"choices\":[null],\"reason\":\"Choose the scene\"}" : JsonSerializer.Serialize(new {
            disposition = scenario == "uncertain" ? "NeedsPlanning" : "Select", choices,
            reason = "Scope evidence does not establish one responsible implementation." });
        using var client = new SelectionClient(response);
        await DeliveryCorrectionCoordinator.ReconcileProjectAsync(f.Project, f.Context(), () => client, default);
        Assert.Empty(f.Created);
        Assert.Equal(0, f.Starts);
        Assert.Null(f.Plans[0].Corrections[0].SourceItemId);
        Assert.Equal("NeedsPlanning", f.Plans[0].Corrections[0].Status);
        Assert.NotEmpty(f.Plans[0].Corrections[0].WaitingReason!);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("competing-source")]
    [InlineData("plan")]
    [InlineData("denied")]
    [InlineData("timeout")]
    [InlineData("provider")]
    public async Task Changed_or_unavailable_selection_never_starts_speculative_work(string scenario)
    {
        var f = PrismCase();
        using var client = new SelectionClient(f.ChooseScene(), () => {
            if (scenario == "source") f.Source = f.Source with { Revision = f.Source.Revision + 1 };
            if (scenario == "competing-source") f.OtherSource = f.OtherSource! with { Revision = f.OtherSource.Revision + 1 };
            if (scenario == "plan") f.Plans[0] = f.Plans[0] with { Revision = f.Plans[0].Revision + 1 };
            if (scenario == "denied") f.DenySelection = true;
            if (scenario == "timeout") throw new OperationCanceledException();
            if (scenario == "provider") throw new HttpRequestException("Unavailable");
        });
        await DeliveryCorrectionCoordinator.ReconcileProjectAsync(f.Project, f.Context(), () => client, default);
        Assert.Empty(f.Created);
        Assert.Equal(0, f.Starts);
        Assert.Null(f.Plans[0].Corrections[0].SourceItemId);
        Assert.Equal("NeedsPlanning", f.Plans[0].Corrections[0].Status);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_without_saving_a_planning_wait()
    {
        var f = PrismCase();
        using var cancellation = new CancellationTokenSource();
        using var client = new SelectionClient(f.ChooseScene(), () => {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DeliveryCorrectionCoordinator.ReconcileProjectAsync(
            f.Project, f.Context(), () => client, cancellation.Token));
        Assert.Empty(f.Effects);
        Assert.Empty(f.Created);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_finding_requires_coverage_and_cross_source_findings_are_not_merged_into_one_repair(bool split)
    {
        var f = PrismCase();
        var first = f.Plans[0].Findings[0];
        var second = first with { Id = Guid.NewGuid(), Summary = split ? "The exact Phaser dependency lockfile is invalid." : "Another placeholder scene sample selects the wrong colour." };
        f.Plans[0] = f.Plans[0] with { Findings = [first, second], Corrections = [f.Plans[0].Corrections[0] with { FindingIds = [first.Id, second.Id] }] };
        var answer = JsonSerializer.Serialize(new { disposition = "Select", reason = "Map the retained findings to their original implementation scopes.", choices = new[] {
            new { findingId = first.Id, sourceItemId = f.Source.Id, scopeEvidence = "Implement the placeholder scene and its sample mapping", findingEvidence = first.Summary },
            new { findingId = second.Id, sourceItemId = split ? f.OtherSource!.Id : f.Source.Id,
                scopeEvidence = split ? "Pin the Phaser dependency and commit its deterministic lockfile." : "Implement the placeholder scene and its sample mapping", findingEvidence = second.Summary }
        } });
        using var client = new SelectionClient(answer);
        await DeliveryCorrectionCoordinator.ReconcileProjectAsync(f.Project, f.Context(), () => client, default);
        Assert.Equal(split ? 0 : 1, f.Starts);
        Assert.Equal(split ? 0 : 3, f.Created.Count);
        if (split) Assert.Null(f.Plans[0].Corrections[0].SourceItemId);
        else Assert.Equal(f.Source.Id, f.Plans[0].Corrections[0].SourceItemId);
    }

    [Fact]
    public async Task Source_selection_retains_decision_evidence_beyond_old_prompt_clipping_limits()
    {
        var f = PrismCase();
        const string scope = "The exact explosive brick sample must match its documented red colour.";
        const string findingTail = "Explosive brick sample is purple instead of the documented red.";
        f.Source = f.Source with { Planning = new([new string('x', 9000), scope],
            [new string('y', 4000), "Explosive brick sample is red"]) };
        var finding = f.Plans[0].Findings[0] with { Summary = new string('z', 4000) + findingTail };
        f.Plans[0] = f.Plans[0] with { Findings = [finding] };
        var answer = JsonSerializer.Serialize(new { disposition = "Select", reason = "The full accepted scope matches the exact finding.",
            choices = new[] { new { findingId = finding.Id, sourceItemId = f.Source.Id,
                scopeEvidence = scope, findingEvidence = findingTail } } });
        using var client = new SelectionClient(answer);
        await DeliveryCorrectionCoordinator.ReconcileProjectAsync(f.Project, f.Context(), () => client, default);
        Assert.Contains(scope, client.Input);
        Assert.Contains(findingTail, client.Input);
        Assert.Contains("Explosive brick sample is red", client.Input);
        Assert.Equal(1, f.Starts);
        Assert.Equal(f.Source.Id, f.Plans[0].Corrections[0].SourceItemId);
    }

    private static Fixture PrismCase()
    {
        var f = new Fixture { Ambiguous = true };
        f.Source = f.Source with { Title = "[chromium-static-scaffold] Create Chromium static scaffold and placeholder scene",
            Planning = new(["Implement the placeholder scene and its sample mapping", "Preserve all accepted project constraints"], ["Scene sample points match documented colours"]) };
        f.OtherSource = f.Source with { Id = f.OtherSource!.Id, Title = "[dependency-pin-phaser] Pin Phaser version and commit lockfile",
            Planning = new(["Pin the Phaser dependency and commit its deterministic lockfile. Out of scope: scene code.",
                "Preserve all accepted project constraints"], ["Installed metadata matches the exact Phaser pin"]) };
        f.Plans[0] = f.Plans[0] with { Findings = [f.Plans[0].Findings[0] with {
            Summary = "SAMPLE_POINTS.brickExplosive maps to brickTough purple while SAMPLE_COLORS.brickExplosive documents red." }] };
        return f;
    }

    private sealed class SelectionClient(string response, Action? onCall = null) : IChatClient
    {
        public int Calls;
        public string Input = "";
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Empty(options!.Tools!);
            Assert.Null(options.MaxOutputTokens);
            Input = messages.Last().Text;
            onCall?.Invoke();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Fixture
    {
        public Guid Project=Guid.NewGuid(),Board=Guid.NewGuid(),Manager=Guid.NewGuid(),OriginalPlan=Guid.NewGuid(),OriginalSprint=Guid.NewGuid();
        public WorkItem Source;
        public WorkItem? OtherSource;
        public List<WorkDeliveryPlanResponse> Plans=[];
        public Dictionary<string,WorkItem> Created=[];
        public WorkSprint? Sprint;
        public int Starts;
        public bool LoseCreateResponse,DenyStart,Ambiguous;
        public bool LoseSelectionResponse, DenySelection;
        public List<string> Effects = [];
        private readonly Guid otherSource=Guid.NewGuid();
        private readonly AgentTestRuntime runtime;
        private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
        public Fixture()
        {
            var story=Guid.NewGuid();var repo=Guid.NewGuid();var finding=Guid.NewGuid();var execution=Guid.NewGuid();
            WorkStageAssignment Assign(string key)=>new(key,"Human",key=="manager-review"?Manager:Guid.NewGuid());
            var prep=new WorkDeliveryTicketStep("source-preparation","completion",Assign("source-preparation"));
            var steps=new[]{new WorkDeliveryTicketStep("quality","quality",Assign("quality")),new("technical-review","review",Assign("technical-review")),new("manager-review","acceptance",Assign("manager-review"))};
            var planning=new WorkItemPlanningSpecification(["Implement the scaffold"],["Scaffold and technical compatibility evidence are correct"]);
            Source=JsonSerializer.Deserialize<WorkItem>("{}",Json)! with {Id=Guid.NewGuid(),Title="Static scaffold",Kind="Task",TypeKey="code-task",Status="Completed",
                ParentItemId=story,SprintId=OriginalSprint,Revision=1,PlanningRevision=1,Planning=planning,
                Delivery=new(repo,planning.Requirements,planning.AcceptanceCriteria){DeliveryKind="Code",DeliveryPlanId=OriginalPlan},
                StageAssignments=[Assign("development"),Assign("technical-review"),Assign("quality"),new("task-integration","PlatformAction",PlatformAction:"source-control.task.integrate.v1")]};
            OtherSource = Source with { Id = otherSource };
            var c=new WorkDeliveryCorrection(Guid.NewGuid(),execution,new string('a',64),Board,Manager,"Pending",1,[finding]);
            var scope=new WorkDeliveryScopeSnapshot("Story",story,Board,1,[Source.Id,otherSource],planning,[prep.Assignment]) {
                TicketWorkflow=new(Guid.NewGuid(),[prep]){Scope="Story",ScopeItemId=story,EvidenceItemIds=[Source.Id],PromoteOnCompletion=true} };
            var release=new WorkDeliveryScopeSnapshot("Release",null,Board,1,[],planning,steps.Select(s=>s.Assignment).ToArray()) {
                TicketWorkflow=new(Guid.NewGuid(),steps){Scope="Release",PromoteOnCompletion=true} };
            Plans.Add(new(OriginalPlan,Project,"Increment",Manager,"Active",1,1,[],[new(repo,"Story",story,"story","release"),new(repo,"Release",null,"release","integration")],
                [scope,release],[new(execution,"Release",null,Board,"Blocked","quality",1,new(new string('a',64),1,[new(repo,"release","integration","a","b","c")],[]),"Wrong sample",[],[])],DateTimeOffset.UtcNow,DateTimeOffset.UtcNow) {
                Corrections=[c],Findings=[new(finding,execution,c.CandidateDigest,"Explosive sample selects the tough brick","Open",[],null)] });
            runtime=new AgentTestRuntime()
                .RegisterCapability<ReadWorkDeliveryPlansRequest,IReadOnlyList<WorkDeliveryPlanResponse>>(WorkDeliveryCapabilities.Read,(r,_)=>Task.FromResult<IReadOnlyList<WorkDeliveryPlanResponse>>(Plans.Where(p=>r.PlanId is null||r.PlanId==p.Id).ToArray()))
                .RegisterCapability<JsonElement,JsonElement>(WorkItemCapabilities.Read,(r,_)=>{
                    if (r.TryGetProperty("itemId",out var id)) return Task.FromResult(JsonSerializer.SerializeToElement(id.GetGuid()==Source.Id?Source:id.GetGuid()==otherSource?OtherSource:Created.Values.Single(i=>i.Id==id.GetGuid()),Json));
                    return Task.FromResult(JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<WorkBoardDetail>("{}",Json)! with {
                        Items=Ambiguous?[Source,OtherSource!]:[Source,..Created.Values] },Json));})
                .RegisterCapability<CreateWorkItemRequest,WorkItem>(WorkItemCapabilities.Create,(r,_)=>{
                    Assert.NotNull(Plans[0].Corrections[0].SourceItemId);
                    if (!Created.ContainsKey(r.IdempotencyKey)) Effects.Add("create");
                    if(!Created.TryGetValue(r.IdempotencyKey,out var item)) Created[r.IdempotencyKey]=item=Source with {Id=Guid.NewGuid(),Title=r.Title,Planning=r.Planning,
                        ParentItemId=r.ParentItemId,Delivery=null,StageAssignments=[],Status="Ready",SprintId=null,TypeKey=r.TypeKey};
                    if(LoseCreateResponse){LoseCreateResponse=false;throw new IOException("Persisted task, response lost");}
                    return Task.FromResult(item);})
                .RegisterCapability<ConfigureWorkDeliveryPlanRequest,WorkDeliveryPlanResponse>(WorkDeliveryCapabilities.Configure,(r,_)=>{
                    var result=Plans[0] with {Id=Guid.NewGuid(),Name=r.Name,Status="Draft",Corrections=[],Findings=[],Executions=[],Branches=r.Branches,
                        Scopes=r.TicketWorkflows.Select(w=>new WorkDeliveryScopeSnapshot(w.Scope,w.ScopeItemId,Board,1,w.EvidenceItemIds,
                            Created.Values.Single(i=>i.Id==w.TicketId).Planning!,w.Steps.Select(s=>s.Assignment).ToArray()){TicketWorkflow=w}).ToArray()};
                    Plans.Add(result);return Task.FromResult(result);})
                .RegisterCapability<FinalizeWorkItemDeliveryRequest,WorkItem>(WorkItemCapabilities.FinalizeDelivery,(r,_)=>{
                    var key=Created.Single(i=>i.Value.Id==r.ItemId).Key;return Task.FromResult(Created[key]=Created[key] with {Delivery=r.Delivery,StageAssignments=r.StageAssignments});})
                .RegisterCapability<CoordinateWorkDeliveryCorrectionRequest,WorkDeliveryPlanResponse>(WorkDeliveryCapabilities.Correct,(r,_)=>{
                    if (DenySelection && r.SourceItemId.HasValue) throw new PlatformCapabilityException(WorkDeliveryCapabilities.Correct, PlatformCapabilityErrorCode.Denied, "Current manager grant revoked");
                    var current=Plans[0].Corrections[0];Assert.Equal(current.Revision,r.ExpectedRevision);
                    if (r.SourceItemId.HasValue) Effects.Add(r.FollowUpPlanId.HasValue ? "bind" : "select");
                    Plans[0]=Plans[0] with {Revision=Plans[0].Revision+1,Status=r.FollowUpPlanId.HasValue?"Paused":Plans[0].Status,Corrections=[current with {
                        Revision=current.Revision+1,SourceItemId=r.SourceItemId??current.SourceItemId,FollowUpPlanId=r.FollowUpPlanId??current.FollowUpPlanId,
                        RemediationTaskIds=r.RemediationTaskIds??current.RemediationTaskIds,WaitingReason=r.WaitingReason,Status=r.FollowUpPlanId.HasValue?"Assigned":"NeedsPlanning"}]};
                    if (LoseSelectionResponse && r.SourceItemId.HasValue && !r.FollowUpPlanId.HasValue) { LoseSelectionResponse=false;throw new IOException("Persisted selection, response lost"); }
                    return Task.FromResult(Plans[0]);})
                .RegisterCapability<ControlWorkDeliveryPlanRequest,WorkDeliveryPlanResponse>(WorkDeliveryCapabilities.Control,(r,_)=>{
                    var index=Plans.FindIndex(p=>p.Id==r.PlanId);return Task.FromResult(Plans[index]=Plans[index] with {Status="Active"});})
                .RegisterCapability<WorkBoardReference,IReadOnlyList<WorkSprint>>(WorkSprintCapabilities.Read,(_,_)=>Task.FromResult<IReadOnlyList<WorkSprint>>(Sprint is null?[]:[Sprint]))
                .RegisterCapability<CreateWorkSprintRequest,WorkSprint>(WorkSprintCapabilities.Create,(r,_)=>Task.FromResult(Sprint??=new(Guid.NewGuid(),Board,r.Name,r.Goal!,"Planned",null,null,null,null,null,1,0,0,0,1)))
                .RegisterCapability<SetWorkItemSprintRequest,WorkItem>(WorkSprintCapabilities.ManageScope,(r,_)=>{var key=Created.Single(i=>i.Value.Id==r.ItemId).Key;return Task.FromResult(Created[key]=Created[key] with {SprintId=r.SprintId});})
                .RegisterCapability<StartWorkSprintExecutionRequest,WorkSprintPreflightResult>(WorkOrchestrationCapabilities.Preflight,(r,_)=>Task.FromResult(new WorkSprintPreflightResult(!DenyStart,Board,r.SprintId,null,DenyStart?[new("staffing","Engineer unavailable")]:[])))
                .RegisterCapability<StartWorkSprintExecutionRequest,WorkSprintExecutionResponse>(WorkOrchestrationCapabilities.Start,(r,_)=>{Starts++;Sprint=Sprint! with {Status="Active"};return Task.FromResult(JsonSerializer.Deserialize<WorkSprintExecutionResponse>("{}",Json)!);});
        }
        public AgentRuntimeContext Context()=>runtime.CreateContext(identity:new(Manager.ToString(),"Producer",null,null,null,[],null,null,null));
        public string ChooseScene() => JsonSerializer.Serialize(new { disposition="Select", reason="The scene sample defect belongs to scaffold implementation; dependency-only work excludes scene code.",
            choices=new[] {new {findingId=Plans[0].Findings[0].Id,sourceItemId=Source.Id,
                scopeEvidence="Implement the placeholder scene and its sample mapping",findingEvidence=Plans[0].Findings[0].Summary}} });
    }
}
