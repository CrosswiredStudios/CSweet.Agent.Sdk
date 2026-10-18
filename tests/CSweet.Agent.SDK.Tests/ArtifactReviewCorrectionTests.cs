using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK.Tests;

public sealed class ArtifactReviewCorrectionTests
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("checklist")] [InlineData("json")] [InlineData("null")]
    [InlineData("identity")] [InlineData("contradiction")]
    public async Task CorrectsSchemaWithExactCriteriaWithoutChangingVerdictOrRereadingSourceOnEachCorrection(string fault)
    {
        var f=new Fixture();var valid=f.Report(false);
        var invalid=fault switch {
            "checklist"=>Encode(valid with {Criteria=Enumerable.Range(0,8).Select(i=>new WorkDeliveryCriterionResult("Checklist "+i,false,"Actual finding remains")).ToArray()}),
            "json"=>"{not valid JSON",
            "null"=>Encode(valid with {Criteria=null!}),
            "identity"=>Encode(valid with {ArtifactId=Guid.NewGuid(),RevisionId=Guid.NewGuid(),Sha256=new string('b',64)}),
            _=>Encode(valid with {Passed=true})
        };
        using var client=new Client((calls,messages)=> {
            if(calls==1)return invalid;
            var feedback=messages.Last().Text;
            Assert.Contains("Correction 1/3",feedback);Assert.Contains("do not alter a verdict or suppress a defect",feedback);
            if(fault=="checklist")Assert.Contains("expected exactly 1 acceptance criteria, received 8",feedback);
            return Encode(valid);
        });
        var outcome=await f.Run(client);
        Assert.Equal(2,client.Calls);Assert.Equal(2,f.ArtifactReads);
        Assert.Equal("changes_requested",outcome.OutcomeCode);
        var report=outcome.Output.Deserialize<WorkArtifactQualityResult>(Json)!;
        Assert.False(report.Passed);Assert.Equal(valid.Findings,report.Findings);
        Assert.Equal(f.Criterion,Assert.Single(report.Criteria).Criterion);
    }

    [Fact]
    public async Task StopsAfterThreeCorrectionsAndReportsArtifactProcessingIssue()
    {
        var f=new Fixture();using var client=new Client((_,_)=>Encode(f.Report(true) with {Criteria=[]}));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Run(client));
        Assert.Equal(4,client.Calls);Assert.Equal(1,f.ArtifactReads);
        Assert.Contains("C-Sweet artifact QA response invalid",error.Message);
        Assert.Contains("after 3 corrections",error.Message);Assert.Contains("No artifact QA verdict was accepted",error.Message);
        Assert.Contains("management",error.Message);Assert.Contains(f.Criterion,error.Message);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReauthorizesPlanningAndExactDocumentBeforeAcceptingVerdict(bool changedDocument)
    {
        var f=new Fixture();using var client=new Client((_,_)=> {
            if(changedDocument)f.Content="Changed document";else f.Item=f.Item with {PlanningRevision=2};
            return Encode(f.Report(true));
        });
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Run(client));
        Assert.Contains("changed during review; no verdict was accepted",error.Message);
    }

    [Fact]
    public async Task RetainsReportLargerThanOldLimitsWithin128000Characters()
    {
        var f=new Fixture();var criteria=Enumerable.Range(0,6).Select(i=>"Exact criterion "+i).ToArray();
        f.Item=f.Item with {Planning=new(["Requirement"],criteria)};
        var report=f.Report(true) with {Criteria=criteria.Select(c=>new WorkDeliveryCriterionResult(c,true,new string('e',9000))).ToArray()};
        Assert.InRange(Encode(report).Length,44001,128000);
        using var client=new Client((_,_)=>Encode(report));var outcome=await f.Run(client);
        Assert.Equal("passed",outcome.OutcomeCode);Assert.Equal(1,client.Calls);
    }

    private static string Encode(WorkArtifactQualityResult report)=>JsonSerializer.Serialize(report,Json);
    private sealed class Fixture
    {
        internal readonly string Criterion="Feature branch exists with a passing build; TD approval recorded before merge.";
        internal readonly Guid Artifact=Guid.NewGuid(),Revision=Guid.NewGuid(),Reviewer=Guid.NewGuid();
        internal string Content="Exact delivered technical review and observed build evidence.";
        internal readonly string Sha;
        internal WorkItem Item;internal int ArtifactReads;
        private readonly AgentTestRuntime runtime;
        internal Fixture() {
            Sha=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Content)));
            Item=JsonSerializer.Deserialize<WorkItem>("{}")! with {Id=Guid.NewGuid(),PlanningRevision=1,Planning=new(["Review eight checklist details"],[Criterion]),
                StageAssignments=[new("quality","AgentInstallation",AgentInstallationId:Reviewer)]};
            runtime=new AgentTestRuntime()
                .RegisterCapability<WorkItemReference,WorkItem>(WorkItemCapabilities.Read,(_,_)=>Task.FromResult(Item))
                .RegisterCapability<JsonElement,ArtifactDocument>(PlatformCapabilities.ArtifactRead,(_,_)=> {
                    ArtifactReads++;
                    return Task.FromResult(new ArtifactDocument(Artifact,"Review","review.v1","Submitted",Revision,Revision,null,null,Item.Id,
                        [new(Revision,1,null,Content,Sha,"Submitted",DateTimeOffset.UtcNow,null,null)]));
                });
        }
        internal WorkArtifactQualityResult Report(bool passed)=>new(Artifact,Revision,Sha,passed,"Substantive exact revision review",
            [new(Criterion,passed,"Observed concrete evidence")],passed?[]:["Preserve this genuine unresolved defect"]);
        internal async Task<WorkExecutionOutcomeV1> Run(Client client) {
            var context=runtime.CreateContext(installationId:Reviewer.ToString());
            var authored=new WorkExecutionOutcomeV1(Guid.NewGuid(),Guid.NewGuid(),WorkExecutionDispositions.Completed,"completed","Exact document",
                new WorkArtifactDelivery(Artifact,Revision,Sha).ToOutput(),[],[]);
            var assignment=JsonSerializer.Deserialize<WorkExecutionAssignment>("{}")! with {Scope=WorkExecutionScopes.Task,SprintId=Guid.NewGuid(),SprintExecutionId=Guid.NewGuid(),PolicyRevisionId=Guid.NewGuid(),
                ItemId=Item.Id,BoardId=Guid.NewGuid(),StageExecutionId=Guid.NewGuid(),AttemptId=Guid.NewGuid(),StageKey="quality",
                Item=JsonSerializer.SerializeToElement(new {deliverySpecificationJson=JsonSerializer.Serialize(new WorkItemDeliverySpecification(Guid.Empty,[],[],[]) {DeliveryKind="Artifact"},Json)}),
                Input=JsonSerializer.SerializeToElement(new WorkExecutionInputV1(Guid.NewGuid(),Guid.NewGuid(),1,Item.Planning),Json),PriorOutcomes=[authored],Evidence=[]};
            return (await ArtifactDeliveryReview.ExecuteAsync(assignment,context,client,default)).Value!.Value.Deserialize<WorkExecutionOutcomeV1>(Json)!;
        }
    }
    private sealed class Client(Func<int,IReadOnlyList<ChatMessage>,string> respond):IChatClient
    {
        internal int Calls;
        public void Dispose(){}public object? GetService(Type type,object? key=null)=>null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,ChatOptions? options=null,CancellationToken cancellationToken=default) {
            cancellationToken.ThrowIfCancellationRequested();Assert.Equal(128000,options!.MaxOutputTokens);Assert.NotNull(options.ResponseFormat);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,respond(++Calls,messages.ToArray()))));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,ChatOptions? options=null,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
}
