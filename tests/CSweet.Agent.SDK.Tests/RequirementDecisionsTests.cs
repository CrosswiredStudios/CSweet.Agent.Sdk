using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class RequirementDecisionsTests
{
    [Theory]
    [InlineData("Windows acceptance coverage", "Revise coverage", "Obtain a compatible environment")]
    [InlineData("Customer research access", "Use consented interviews", "Request dataset access")]
    public async Task InferenceAuthorsRelevantChoicesAndCodeBindsTheDecision(string requirement, string first, string second)
    {
        var calls = 0;
        var runtime = new AgentTestRuntime().RegisterCapability<JsonElement, object>(PlatformCapabilities.LlmChatStream, (r, _) => {
            calls++;
            Assert.Contains(requirement, r.GetRawText());
            return Task.FromResult<object>(new { text = JsonSerializer.Serialize(new RequirementDecisionDraft(
                "How should we resolve " + requirement + "?", "Affected work waits; independent planning continues.",
                [new(first, "Adjust the actual requirement", "Ask the authorized owner to record the exact change"),
                 new(second, "Obtain missing access or capacity", "Discover supported setup and qualified assistance before requesting authority")], 1), JsonSerializerOptions.Web) });
        });
        using var client = runtime.CreateContext().CreateChatClient(new(Guid.NewGuid(), "test"));
        var facts = JsonSerializer.SerializeToElement(new { requirement });
        var draft = await RequirementDecisions.AssessAsync(client, "Accepted scope", facts, null);
        var project = Guid.NewGuid();
        var request = RequirementDecisions.Bind(draft, project, "external.requirement", "owner-required", [], null, "bound-by-code", facts);
        Assert.Equal(project, request.WorkstreamId); Assert.Equal("owner-required", request.AuthorityRuleKey);
        Assert.Equal("bound-by-code", request.IdempotencyKey); Assert.Equal("choice-1", request.RecommendedOptionId);
        Assert.Equal(first, request.Options[0].Label); Assert.Equal(second, request.Options[1].Label);
        Assert.Contains(request.Options, o => o.Id == "provide-direction"); Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Superseded")]
    public async Task FollowThroughCannotExecuteAnUndecidedOrReplacedDirection(string status)
    {
        var decision = JsonSerializer.Deserialize<DecisionRecord>("{}")! with { Id = Guid.NewGuid(), WorkstreamId = Guid.NewGuid(), Status = status, Revision = 1 };
        var runtime = new AgentTestRuntime().RegisterCapability<ReadDecisionRequest, IReadOnlyList<DecisionRecord>>(PlatformCapabilities.DecisionRead,
            (_, _) => Task.FromResult<IReadOnlyList<DecisionRecord>>([decision]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => RequirementDecisions.FollowThroughAsync(decision,
            "Scope", JsonSerializer.SerializeToElement(new {}), runtime.CreateContext(), new(Guid.NewGuid(), "test"), null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedDirectionUsesDiscoveredToolsOnceAndStopsDispatchIfTheDecisionChanges(bool replaced)
    {
        var data = JsonSerializer.SerializeToElement(new { contract = RequirementDecisions.Contract });
        var current = JsonSerializer.Deserialize<DecisionRecord>("{}")! with { Id = Guid.NewGuid(), WorkstreamId = Guid.NewGuid(),
            Revision = 2, Status = "Decided", TypeData = data, SelectedOptionId = "choice-0" };
        var decision = current; var modelCalls = 0; var effects = 0;
        var states = new Dictionary<string, AgentOperatingStateResponse>();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ReadDecisionRequest, IReadOnlyList<DecisionRecord>>(PlatformCapabilities.DecisionRead,
                (_, _) => Task.FromResult<IReadOnlyList<DecisionRecord>>([current]))
            .RegisterCapability<object, object>("external.setup.request", (_, _) => {
                effects++; return Task.FromResult<object>(new { status = "Requested", ready = false });
            }, toolName: "request_setup", modelVisible: true)
            .RegisterCapability<AgentOperatingStateReadRequest, AgentOperatingStateReadResponse>(PlatformCapabilities.AgentOperatingStateRead,
                (r, _) => Task.FromResult(new AgentOperatingStateReadResponse(states.GetValueOrDefault(r.StateKey))))
            .RegisterCapability<AgentOperatingStateWriteRequest, AgentOperatingStateResponse>(PlatformCapabilities.AgentOperatingStateWrite, (r, _) => {
                var now = DateTimeOffset.UtcNow;
                var value = new AgentOperatingStateResponse(Guid.NewGuid(), r.StateKey, r.SchemaId, r.SchemaVersion, r.Status,
                    r.SourceRevisions, r.ConditionCodes, r.DecisionFingerprint, r.OpenCommitmentCorrelations, r.AttentionReviewId, r.Payload, 1, now, now);
                states[r.StateKey] = value; return Task.FromResult(value);
            })
            .RegisterCapability<JsonElement, object>(PlatformCapabilities.LlmChatStream, (_, _) => {
                modelCalls++;
                if (modelCalls == 1) {
                    if (replaced) current = current with { Status = "Superseded" };
                    return Task.FromResult<object>(new { contents = new[] { new PlatformChatContent("function_call", CallId: "setup-call",
                        Name: "request_setup", Arguments: new Dictionary<string, JsonElement>()) }, finishReason = "tool_calls" });
                }
                return Task.FromResult<object>(new { text = JsonSerializer.Serialize(new {
                    summary = replaced ? "Direction changed; no setup dispatched." : "Setup requested; verification remains outstanding.",
                    holdAffectedWork = replaced
                }, JsonSerializerOptions.Web) });
            });
        var context = runtime.CreateContext();
        var report = await RequirementDecisions.FollowThroughAsync(decision, "Accepted work", data, context, new(Guid.NewGuid(), "test"), null);
        Assert.Equal(replaced ? 0 : 1, effects);
        Assert.Equal(replaced, report.HoldAffectedWork);
        if (!replaced) {
            var calls = modelCalls;
            var replay = await RequirementDecisions.FollowThroughAsync(decision, "Accepted work", data, context, new(Guid.NewGuid(), "test"), null);
            Assert.Equal(report.Summary, replay.Summary); Assert.Equal(calls, modelCalls); Assert.Equal(1, effects);
        }
    }
}
