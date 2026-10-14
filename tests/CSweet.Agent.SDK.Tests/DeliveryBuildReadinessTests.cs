using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.Tests;

public sealed class DeliveryBuildReadinessTests
{
    [Theory]
    [InlineData("Queued", "awaiting-build", false)]
    [InlineData("Running", "awaiting-build", false)]
    [InlineData("Succeeded", "approved", false)]
    [InlineData("Failed", "blocked", false)]
    [InlineData("stale-scope", "blocked", true)]
    [InlineData("wrong-principal", "blocked", true)]
    [InlineData("wrong-stage", "blocked", true)]
    [InlineData("wrong-recipe", "blocked", false)]
    [InlineData("wrong-source", "blocked", false)]
    [InlineData("missing-provenance", "blocked", false)]
    [InlineData("missing-outputs", "blocked", false)]
    [InlineData("denied", "blocked", false)]
    public async Task Builds_require_current_authority_and_successful_exact_candidate_evidence(string state, string expected, bool noBuild)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var installation = Guid.NewGuid(); var employee = Guid.NewGuid(); var project = Guid.NewGuid(); var executionId = Guid.NewGuid(); var planId = Guid.NewGuid();
        var candidate = new WorkDeliveryCandidate(new string('a', 64), 1,
            [new(Guid.NewGuid(), "release", "main", new string('b', 40), new string('c', 40), new string('d', 40))], []);
        var stage = JsonSerializer.Deserialize<WorkStageExecutionResponse>("{}")! with {
            Id = Guid.NewGuid(), StageKey = "build-readiness", OrganizationUserId = employee,
            AgentInstallationId = state == "wrong-principal" ? Guid.NewGuid() : installation };
        var execution = JsonSerializer.Deserialize<WorkDeliveryExecutionResponse>("{}")! with {
            Id = executionId, Scope = "Release", Candidate = candidate, Stages = [stage],
            CurrentStageKey = state == "wrong-stage" ? "quality" : "build-readiness" };
        var specification = new WorkDeliveryBuildSpecification(Guid.NewGuid(), Guid.NewGuid(), "web-build", "web", JsonSerializer.SerializeToElement(new { }));
        var plan = JsonSerializer.Deserialize<WorkDeliveryPlanResponse>("{}")! with {
            Id = planId, WorkstreamId = project, Status = "Active", ScopeRevision = state == "stale-scope" ? 2 : 1, Executions = [execution],
            Branches = [new(candidate.Repositories[0].RepositoryId, "Release", null, "release", "main") { Build = specification }],
            Scopes = [new("Release", null, Guid.NewGuid(), 1, [], new(["Build exact source"], ["Packaging exists"]), [])] };
        var requests = new List<RequestBuildV2Request>();
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ReadWorkDeliveryPlansRequest, IReadOnlyList<WorkDeliveryPlanResponse>>(WorkDeliveryCapabilities.Read,
                (_, _) => Task.FromResult<IReadOnlyList<WorkDeliveryPlanResponse>>([plan]))
            .RegisterCapability<RequestBuildV2Request, DeliveryBuildV2>(PlatformCapabilities.BuildRequest, (request, _) => {
                requests.Add(request);
                if (state == "denied") throw new PlatformCapabilityException(PlatformCapabilities.BuildRequest,
                    PlatformCapabilityErrorCode.Denied, "Build grant is required.");
                var source = state == "wrong-source" ? new string('e', 40) : request.SourceRevision;
                var status = state is "Queued" or "Running" or "Failed" ? state : "Succeeded";
                return Task.FromResult(JsonSerializer.Deserialize<DeliveryBuildV2>("{}")! with {
                    Id = Guid.NewGuid(), WorkstreamId = project, RepositoryId = request.RepositoryId, SourceRevision = source,
                    ToolchainDefinitionId = request.ToolchainDefinitionId, ProviderInstallationId = request.ProviderInstallationId,
                    RecipeKey = state == "wrong-recipe" ? "other" : request.RecipeKey, TargetKey = request.TargetKey, Configuration = request.Configuration,
                    Status = status, Outputs = state == "missing-outputs" ? [] : [new("index.html", new string('f', 64), 100, "text/html", "web")],
                    Provenance = state == "missing-provenance" ? null : new("provider", "1.0", "digest", "image", JsonSerializer.SerializeToElement(new { }),
                        "lock", ["build"], source, new string('f', 64), []) });
            });
        var assignment = new WorkExecutionAssignment(executionId, stage.Id, Guid.NewGuid(), Guid.NewGuid(), project, Guid.NewGuid(), null,
            "Release", planId, 1, null, null, 1, "Release", null, "build-readiness", 0, 1, DateTimeOffset.UtcNow.AddHours(1), "Build candidate",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), [], [], candidate) {
                OrganizationUserId = employee, AgentInstallationId = installation, PlanningRevision = 1, PermittedOutcomes = ["approved", "changes_requested", "awaiting-build"] };
        var context = runtime.CreateContext(assignment.OrganizationId.ToString(), installation.ToString());
        var result = await DeliveryBuildReadiness.ExecuteAsync(assignment, context, default);
        var outcome = result.Value!.Value.Deserialize<WorkExecutionOutcomeV1>(json)!;
        Assert.Equal(expected, outcome.OutcomeCode);
        Assert.Equal(noBuild ? 0 : 1, requests.Count);
        if (expected is "approved" or "awaiting-build") {
            var firstKey = requests[0].IdempotencyKey;
            await DeliveryBuildReadiness.ExecuteAsync(assignment, context, default);
            Assert.Equal(firstKey, requests[1].IdempotencyKey);
            Assert.Equal(candidate.Repositories[0].CandidateCommitSha, requests[0].SourceRevision);
        }
    }
}
