using System.Text.Json;
using Microsoft.Extensions.AI;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class WorkPlanningJudgmentsTests
{
    [Fact]
    public async Task EffortAndAvailabilityAreIndependentJudgmentsAndMalformedReplyGetsOneCorrection()
    {
        using var client=new Replies("{\"estimates\":[],\"availableCapacity\":10,\"assumptions\":[],\"blockers\":[]}",
            "{\"estimates\":[{\"points\":8,\"confidence\":\"low\",\"rationale\":\"Collision interactions need an experiment\"}],\"capacityAssessment\":{\"offeredPoints\":2,\"rationale\":\"Other review commitments occupy capacity\"},\"assumptions\":[],\"blockers\":[]}");
        var result=await WorkPlanningJudgments.AssessAsync(client,[JsonSerializer.SerializeToElement(new {requirement="High-speed collision"})],
            JsonSerializer.SerializeToElement(new {existingReviews=3}),null,default);
        Assert.Equal(8,result.Estimates.Single().Points);Assert.Equal(2,result.AvailableCapacity);Assert.Equal(2,client.Calls);
    }

    [Fact]
    public void UnavailableCapacityIsValidAndDemandDoesNotComputeAvailability()
    {
        var value=new WorkPlanningJudgments.Assessment([new(13,"low","Novel renderer")],0,["No execution capacity"],[]);
        Assert.Same(value,WorkPlanningJudgments.Validate(value,1));
        Assert.Throws<AgentModelJsonException>(()=>WorkPlanningJudgments.Validate(value with {Estimates=[]},1));
    }

    [Fact]
    public async Task RetainedUnknownZeroRequiresFreshJudgmentAndNeverBecomesAnAutomaticAllocation()
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "shardfall-zero-capacity.json")));
        var legacy = WorkCapacityAssessment.FromEvidence(payload, payload.GetProperty("AvailableSprintCapacity").GetDecimal());
        Assert.Null(legacy.OfferedPoints);
        Assert.Equal("Unknown", legacy.State);
        using var client = new Replies("""{"estimates":[{"points":8,"confidence":"medium","rationale":"Compare alternatives and prepare a source-bound research note"}],"capacityAssessment":{"offeredPoints":null,"rationale":"A conflicting commitment's priority is unresolved","missingEvidence":"Ask the responsible manager which commitment takes priority"},"assumptions":[],"blockers":[]}""");
        var result = await WorkPlanningJudgments.AssessAsync(client, [JsonSerializer.SerializeToElement(new { outcome = "Research note" })],
            JsonSerializer.SerializeToElement(new { legacy, actualCompetingWork = "Another committed investigation" }), null, default);
        Assert.Null(result.CapacityAssessment!.OfferedPoints);
        Assert.Equal(0, result.AvailableCapacity); // Compatibility projection only; new consumers retain Unknown.
        Assert.Equal(8, result.Estimates.Single().Points);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task RealUnavailabilityOrPartialOfferIsPreservedInsteadOfFillingDemand(int points)
    {
        using var client = new Replies(JsonSerializer.Serialize(new { estimates = new[] { new { points = 13, confidence = "low", rationale = "Novel integration" } },
            capacityAssessment = new { offeredPoints = points, rationale = "Existing committed work limits my offer" }, assumptions = Array.Empty<string>(), blockers = Array.Empty<string>() }));
        var result = await WorkPlanningJudgments.AssessAsync(client, [JsonSerializer.SerializeToElement(new { outcome = "Integration" })],
            JsonSerializer.SerializeToElement(new { competingWork = "Current delivery" }), null, default);
        Assert.Equal(points, result.CapacityAssessment!.OfferedPoints);
        Assert.Equal(13, result.Estimates.Single().Points);
    }

    [Fact]
    public void CorrectionLineageRequiresPositiveEvidenceAndNeverTreatsCancellationAsAcceptance()
    {
        WorkDeliveryPlanResponse Plan(string status)=>JsonSerializer.Deserialize<WorkDeliveryPlanResponse>("{}")! with {
            Id=Guid.NewGuid(),Status=status,Corrections=[],Findings=[]};
        var old=Plan("Cancelled");var next=Plan("Completed");
        Assert.Null(FinalProductDelivery.AcceptedSuccessor(old,[old,next]));
        old=old with {Corrections=[new(Guid.NewGuid(),Guid.NewGuid(),"rejected",Guid.NewGuid(),Guid.NewGuid(),"Completed",2,[],FollowUpPlanId:next.Id)]};
        Assert.Equal(next.Id,FinalProductDelivery.AcceptedSuccessor(old,[old,next])!.Id);
        Assert.Null(FinalProductDelivery.AcceptedSuccessor(old,[old,next with {Status="Active"}]));
    }

    private sealed class Replies(params string[] replies):IChatClient
    {
        public int Calls {get;private set;}
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,ChatOptions? options=null,CancellationToken cancellationToken=default)=>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,replies[Math.Min(Calls++,replies.Length-1)])));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,ChatOptions? options=null,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public object? GetService(Type serviceType,object? serviceKey=null)=>null;
        public void Dispose(){}
    }
}
