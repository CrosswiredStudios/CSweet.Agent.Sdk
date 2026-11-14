using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class WorkContributionsTests
{
    [Theory]
    [InlineData("narrative-designer")]
    [InlineData("playtest-researcher")]
    public async Task ScopedAdviceUsesTheSameContractAcrossExpertiseAndReplaysWithoutInventingApproval(string expertise)
    {
        var f=new Fixture();
        var first=await f.Run(expertise);var replay=await f.Run(expertise);
        Assert.Equal("Completed",first.Disposition);Assert.Equal(WorkContributions.AnswerType,first.Artifact!.Type);
        Assert.Equal(first.Artifact.Payload.GetRawText(),replay.Artifact!.Payload.GetRawText());Assert.Equal(1,f.Calls);
        f.CurrentSource=Guid.NewGuid();Assert.Equal("Blocked",(await f.Run(expertise)).Disposition);Assert.Equal(1,f.Calls);
    }

    [Fact]
    public async Task LossOfManagerAuthorityDuringJudgmentPreventsUseOfTheAnswer()
    {
        var f=new Fixture();f.OnModel=()=>f.CurrentManager=Guid.NewGuid();
        Assert.Equal("Blocked",(await f.Run("qualified generalist")).Disposition);
    }

    [Fact]
    public void PlanningStructureIsCheckedWithoutAQualityAgentAndStaleEffortIsRejected()
    {
        var item=JsonSerializer.Deserialize<WorkItem>("{}")! with {Id=Guid.NewGuid(),PlanningRevision=2,
            Planning=new(["Produce the design"],["The design answers the question"]),EstimatePoints=3,
            EstimateProvenance=new(Guid.NewGuid(),Guid.NewGuid(),Guid.Empty,0,"evidence",0.7m) {PlanningRevision=1},
            StageAssignments=[new("specialist-execution","Human",Guid.NewGuid())]};
        Assert.Contains(WorkPlanningReadiness.Validate([item]),i=>i.Code=="estimate-provenance-missing");
        Assert.Empty(WorkPlanningReadiness.Validate([item with {EstimateProvenance=item.EstimateProvenance with {PlanningRevision=2}}]));
    }

    private sealed class Fixture
    {
        private readonly Guid project=Guid.NewGuid(),board=Guid.NewGuid(),document=Guid.NewGuid(),revision=Guid.NewGuid();
        private readonly AgentCoordinationParticipant manager=new(Guid.NewGuid(),Guid.NewGuid(),"Manager","Producer");
        private readonly AgentTestRuntime runtime;
        private readonly AgentCoordinationTurnRequest request;
        public Guid CurrentManager,CurrentSource;
        public int Calls;
        public Action? OnModel;
        public Fixture()
        {
            CurrentManager=manager.OrganizationUserId;CurrentSource=revision;
            var states=new Dictionary<string,AgentOperatingStateResponse>();
            runtime=new AgentTestRuntime()
                .RegisterCapability<WorkBoardReference,WorkBoardDetail>(WorkItemCapabilities.Read,(_,_)=>Task.FromResult(
                    JsonSerializer.Deserialize<WorkBoardDetail>("{}")! with {Board=JsonSerializer.Deserialize<WorkBoardSummary>("{}")! with {
                        Id=board,WorkstreamId=project,ManagerOrganizationUserId=CurrentManager},Items=[]}))
                .RegisterCapability<object,ArtifactDocument>(PlatformCapabilities.ArtifactRead,(_,_)=>Task.FromResult(
                    new ArtifactDocument(document,"Approved scope","scope","Accepted",revision,null,CurrentSource,null,null,
                        [new(revision,1,null,"Explore feel and interacting systems; observations still require a playtest.","digest","Accepted",DateTimeOffset.UtcNow,null,null)]) {WorkstreamId=project}))
                .RegisterCapability<AgentOperatingStateReadRequest,AgentOperatingStateReadResponse>(PlatformCapabilities.AgentOperatingStateRead,(r,_)=>Task.FromResult(new AgentOperatingStateReadResponse(states.GetValueOrDefault(r.StateKey))))
                .RegisterCapability<AgentOperatingStateWriteRequest,AgentOperatingStateResponse>(PlatformCapabilities.AgentOperatingStateWrite,(r,_)=> {
                    var now=DateTimeOffset.UtcNow;var value=new AgentOperatingStateResponse(Guid.NewGuid(),r.StateKey,r.SchemaId,r.SchemaVersion,r.Status,r.SourceRevisions,r.ConditionCodes,r.DecisionFingerprint,r.OpenCommitmentCorrelations,r.AttentionReviewId,r.Payload,1,now,now);
                    states[r.StateKey]=value;return Task.FromResult(value);
                })
                .RegisterCapability<JsonElement,object>(PlatformCapabilities.LlmChatStream,(_,_)=> {
                    Calls++;OnModel?.Invoke();return Task.FromResult<object>(new {text="{\"assessment\":\"Interaction warrants a bounded experiment\",\"proposedChange\":\"Try a small playable slice\",\"evidenceNeeded\":\"Observed sessions against the exact build\"}"});
                });
            var self=new AgentCoordinationParticipant(Guid.NewGuid(),Guid.NewGuid(),"Specialist","Expert");
            request=new(Guid.NewGuid(),1,1,"Scoped advice","Assess a question",[],self,manager,false,
                [new(Guid.NewGuid(),0,manager.OrganizationUserId,"Continue","Please assess",DateTimeOffset.UtcNow,
                    new(WorkContributions.RequestType,"1.0","question",1,true,JsonSerializer.SerializeToElement(new WorkContributionRequest(project,board,"feel","What should we learn first?",[new(document,revision,"digest")])) ,"artifact"))]) {
                SourceKind="Board",BoardSource=new(board),WorkContext=new(Guid.NewGuid(),project,null,board,null,null,null,Guid.NewGuid(),null,null)};
        }
        public Task<AgentCoordinationTurnResult> Run(string expertise)=>WorkContributions.ExecuteAsync(request,runtime.CreateContext(),new(Guid.NewGuid(),"test"),expertise,null,default);
    }
}
