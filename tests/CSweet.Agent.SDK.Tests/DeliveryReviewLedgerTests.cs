using System.Text.Json;
using System.Text.Json.Nodes;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class DeliveryReviewLedgerTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task IdempotentWriteCannotHideThePreviouslyRetainedReviewJudgment()
    {
        var itemId=Guid.NewGuid();var plan=Guid.NewGuid();var publication=Guid.NewGuid();var repo=Guid.NewGuid();
        var assignment=JsonSerializer.Deserialize<WorkExecutionAssignment>("{}")! with {
            Scope="Task",ItemId=itemId,BoardId=Guid.NewGuid(),SprintId=Guid.NewGuid(),SprintExecutionId=Guid.NewGuid(),PolicyRevisionId=Guid.NewGuid(),
            StageExecutionId=Guid.NewGuid(),AttemptId=Guid.NewGuid(),AssignmentRevision=1,PlanningRevision=1,StageKey="technical-review",DeliveryPlanId=plan };
        var item=JsonSerializer.Deserialize<WorkItem>("{}")! with {
            Id=itemId,PlanningRevision=1,Planning=new(["Implement"],["Works"]),
            Delivery=new(repo,["Implement"],["Works"]) {DeliveryPlanId=plan,BaseBranch="story"},
            StageAssignments=[new("technical-review","AgentInstallation",AgentInstallationId:Guid.Parse("00000000-0000-0000-0000-000000000001"))] };
        var reference=new GitMergeReview(publication,repo,itemId,"Test",new string('a',40),null,"",[],[],"AwaitingValidation") {
            TargetBranch="story",TargetCommitSha=new string('b',40),PatchDigest=new string('c',64),ChangedFiles=["source.js"] };
        var runtime=new AgentTestRuntime().RegisterCapability<WorkItemReference,WorkItem>(WorkManagementCapabilityNames.ItemRead,(_,_)=>Task.FromResult(item))
            .RegisterCapability<ReviewGitMergeRequest,GitMergeReview>(GitMergeCapabilities.Review,(r,_)=>Task.FromResult(r.DiffRead is null?reference:
                reference with {DiffPage=new(reference.PatchDigest!,0,3,"abc",null)}))
            .RegisterCapability<AgentOperatingStateReadRequest,AgentOperatingStateReadResponse>(PlatformCapabilities.AgentOperatingStateRead,
                (_,_)=>Task.FromResult(new AgentOperatingStateReadResponse(null)))
            .RegisterCapability<AgentOperatingStateWriteRequest,AgentOperatingStateResponse>(PlatformCapabilities.AgentOperatingStateWrite,(r,_)=>{
                var payload=JsonNode.Parse(r.Payload.GetRawText())!;
                payload["content"]!["summary"]="First writer retained a different judgment.";
                var now=DateTimeOffset.UtcNow;
                return Task.FromResult(new AgentOperatingStateResponse(Guid.NewGuid(),r.StateKey,r.SchemaId,r.SchemaVersion,r.Status,r.SourceRevisions,
                    r.ConditionCodes,r.DecisionFingerprint,r.OpenCommitmentCorrelations,r.AttentionReviewId,
                    JsonSerializer.SerializeToElement(payload),1,now,now));
            });
        using var client=new BranchDiffTechnicalReviewTests.Client((_,_)=>JsonSerializer.Serialize(new BranchDiffTechnicalReview.PageContent("Another judgment",[],[],[]),Json));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>DeliveryTaskTechnicalReview.ExecuteAsync(assignment,runtime.CreateContext(),client,default));
        Assert.Contains("accepted ledger",error.Message);Assert.Equal(1,client.Calls);
    }
}
