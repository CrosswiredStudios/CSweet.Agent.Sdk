using System.Text.Json;
using CSweet.WorkManagement.Contracts;

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

    private sealed class Fixture
    {
        public Guid Project=Guid.NewGuid(),Board=Guid.NewGuid(),Manager=Guid.NewGuid(),OriginalPlan=Guid.NewGuid(),OriginalSprint=Guid.NewGuid();
        public WorkItem Source;
        public List<WorkDeliveryPlanResponse> Plans=[];
        public Dictionary<string,WorkItem> Created=[];
        public WorkSprint? Sprint;
        public int Starts;
        public bool LoseCreateResponse,DenyStart,Ambiguous;
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
                    if (r.TryGetProperty("itemId",out var id)) return Task.FromResult(JsonSerializer.SerializeToElement(id.GetGuid()==Source.Id?Source:Created.Values.Single(i=>i.Id==id.GetGuid()),Json));
                    return Task.FromResult(JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<WorkBoardDetail>("{}",Json)! with {
                        Items=Ambiguous?[Source,Source with {Id=otherSource}]:[Source,..Created.Values] },Json));})
                .RegisterCapability<CreateWorkItemRequest,WorkItem>(WorkItemCapabilities.Create,(r,_)=>{
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
                    var current=Plans[0].Corrections[0];Plans[0]=Plans[0] with {Status=r.FollowUpPlanId.HasValue?"Paused":Plans[0].Status,Corrections=[current with {
                        Revision=current.Revision+1,SourceItemId=r.SourceItemId??current.SourceItemId,FollowUpPlanId=r.FollowUpPlanId??current.FollowUpPlanId,
                        RemediationTaskIds=r.RemediationTaskIds??current.RemediationTaskIds,WaitingReason=r.WaitingReason,Status=r.FollowUpPlanId.HasValue?"Executing":"NeedsPlanning"}]};return Task.FromResult(Plans[0]);})
                .RegisterCapability<ControlWorkDeliveryPlanRequest,WorkDeliveryPlanResponse>(WorkDeliveryCapabilities.Control,(r,_)=>{
                    var index=Plans.FindIndex(p=>p.Id==r.PlanId);return Task.FromResult(Plans[index]=Plans[index] with {Status="Active"});})
                .RegisterCapability<WorkBoardReference,IReadOnlyList<WorkSprint>>(WorkSprintCapabilities.Read,(_,_)=>Task.FromResult<IReadOnlyList<WorkSprint>>(Sprint is null?[]:[Sprint]))
                .RegisterCapability<CreateWorkSprintRequest,WorkSprint>(WorkSprintCapabilities.Create,(r,_)=>Task.FromResult(Sprint??=new(Guid.NewGuid(),Board,r.Name,r.Goal!,"Planned",null,null,null,null,null,1,0,0,0,1)))
                .RegisterCapability<SetWorkItemSprintRequest,WorkItem>(WorkSprintCapabilities.ManageScope,(r,_)=>{var key=Created.Single(i=>i.Value.Id==r.ItemId).Key;return Task.FromResult(Created[key]=Created[key] with {SprintId=r.SprintId});})
                .RegisterCapability<StartWorkSprintExecutionRequest,WorkSprintPreflightResult>(WorkOrchestrationCapabilities.Preflight,(r,_)=>Task.FromResult(new WorkSprintPreflightResult(!DenyStart,Board,r.SprintId,null,DenyStart?[new("staffing","Engineer unavailable")]:[])))
                .RegisterCapability<StartWorkSprintExecutionRequest,WorkSprintExecutionResponse>(WorkOrchestrationCapabilities.Start,(r,_)=>{Starts++;Sprint=Sprint! with {Status="Active"};return Task.FromResult(JsonSerializer.Deserialize<WorkSprintExecutionResponse>("{}",Json)!);});
        }
        public AgentRuntimeContext Context()=>runtime.CreateContext(identity:new(Manager.ToString(),"Producer",null,null,null,[],null,null,null));
    }
}
