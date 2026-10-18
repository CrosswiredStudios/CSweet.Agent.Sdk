using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Requests certified builds for an authorized exact release candidate without host tool access.</summary>
public static class DeliveryBuildReadiness
{
    /// <summary>Advertises explicit stage support in addition to canonical execution; declarations grant no authority.</summary>
    public const string ExecutionCapability = "work.execution.build-readiness.v1";

    /// <summary>Execute one bounded build readiness attempt; pending builds resume through durable platform events.</summary>
    public static async Task<AgentWorkResult> ExecuteAsync(WorkExecutionAssignment assignment,
        AgentRuntimeContext context, CancellationToken ct)
    {
        try
        {
            if ((assignment.Scope != WorkExecutionScopes.Release && assignment.Scope != WorkExecutionScopes.Ticket) ||
                (assignment.Handler is not ("build-readiness" or "compute-test") && assignment.StageKey != "build-readiness") ||
                assignment.DeliveryPlanId is not { } planId || assignment.Candidate is not { } candidate || candidate.Repositories.Count == 0)
                throw new InvalidOperationException("Build readiness requires the exact assigned release and complete repository candidate.");
            var plan = (await context.Platform.Work.ReadDeliveryPlansAsync(new(assignment.WorkstreamId, planId), ct)).Single();
            var execution = plan.Executions.Single(x => x.Id == assignment.ExecutionId);
            if (plan.Status != "Active" || plan.ScopeRevision != assignment.ScopeRevision || execution.Candidate?.Digest != candidate.Digest ||
                execution.CurrentStageKey != assignment.StageKey || execution.Stages.LastOrDefault() is not { } stage ||
                stage.Id != assignment.StageExecutionId || stage.StageKey != assignment.StageKey ||
                stage.AgentInstallationId != assignment.AgentInstallationId || stage.OrganizationUserId != assignment.OrganizationUserId ||
                stage.AgentInstallationId.ToString() != context.InstallationId || plan.WorkstreamId != assignment.WorkstreamId)
                throw new UnauthorizedAccessException("The build assignment or candidate changed.");
            if (plan.Branches.Where(x => x.Scope == assignment.Scope && x.ItemId == assignment.ItemId).All(x => x.ReadinessMode == "compute-test"))
                return await DeliveryComputeTest.ExecuteAuthorizedAsync(assignment, plan, context, ct);
            var builds = new List<DeliveryBuildV2>();
            foreach (var repository in candidate.Repositories)
            {
                var specification = plan.Branches.Single(x => x.Scope == assignment.Scope && x.ItemId == assignment.ItemId && x.RepositoryId == repository.RepositoryId).Build
                    ?? throw new InvalidOperationException("Ask the architect to configure the certified build recipe and packaging target.");
                var build = await context.Platform.RequestBuildAsync(new(plan.WorkstreamId, null, specification.ToolchainDefinitionId,
                    specification.ProviderInstallationId, repository.RepositoryId, repository.CandidateCommitSha, specification.RecipeKey,
                    specification.TargetKey, specification.Configuration, 3, $"release-build:{execution.Id:N}:{repository.RepositoryId:N}:{candidate.Digest}"), ct);
                if (build.WorkstreamId != plan.WorkstreamId || build.RepositoryId != repository.RepositoryId ||
                    build.SourceRevision != repository.CandidateCommitSha || build.ToolchainDefinitionId != specification.ToolchainDefinitionId ||
                    build.ProviderInstallationId != specification.ProviderInstallationId || build.RecipeKey != specification.RecipeKey ||
                    build.TargetKey != specification.TargetKey || build.Configuration.GetRawText() != specification.Configuration.GetRawText())
                    throw new InvalidOperationException("The certified build identity does not match the configured release candidate.");
                builds.Add(build);
            }
            if (builds.Any(x => x.Status is "Queued" or "Claimed" or "Running"))
                return AgentWorkResult.Success(new WorkExecutionOutcomeV1(assignment.StageExecutionId, assignment.AttemptId,
                    WorkExecutionDispositions.Completed, "awaiting-build", "Certified candidate builds are pending; completion events wake delivery recovery.",
                    JsonSerializer.SerializeToElement(new WorkDeliveryBuildPending(candidate.Digest, builds.Select(x => x.Id).ToArray()), new JsonSerializerOptions(JsonSerializerDefaults.Web)), [], []));
            if (builds.Any(x => x.Status != "Succeeded" || x.SourceRevision != candidate.Repositories.Single(r => r.RepositoryId == x.RepositoryId).CandidateCommitSha ||
                x.Outputs.Count == 0 || x.Provenance?.SourceRevision != x.SourceRevision ||
                string.IsNullOrWhiteSpace(x.Provenance.NormalizedOutputManifestHash)))
                throw new InvalidOperationException("One or more release repositories lack successful candidate-bound packaging and provenance.");
            var scope = plan.Scopes.Single(x => x.Scope == assignment.Scope && x.ItemId == assignment.ItemId);
            var summary = "Certified provider builds produced exact-candidate packaging and provenance for every participating repository. Full release regression and technical acceptance must follow.";
            return DeliveryScopeReview.Outcome(assignment, new(candidate.Digest, true, summary,
                scope.Planning.AcceptanceCriteria.Select(c => new WorkDeliveryCriterionResult(c, true,
                    "Build readiness evidence only: " + string.Join("; ", builds.Select(b => $"{b.RepositoryId:D}: build {b.Id:D}, commit {b.SourceRevision}, manifest {b.Provenance!.NormalizedOutputManifestHash}")))).ToArray(), [])
                { BuildIds = builds.Select(x => x.Id).ToArray() });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        { return AgentWorkResult.Success(new WorkExecutionOutcomeV1(assignment.StageExecutionId, assignment.AttemptId,
            WorkExecutionDispositions.Blocked, "blocked", error.Message, JsonSerializer.SerializeToElement(new { }), [], [error.Message])); }
    }
}

