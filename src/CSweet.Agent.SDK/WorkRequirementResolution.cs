using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>One inference-authored decision for one material work dependency, independent of wording and wake identity.</summary>
public static class WorkRequirementResolution
{
    private sealed record Reference(Guid DecisionId);
    public static async Task<DecisionRecord> RequestAsync(Guid project, string type, string authority,
        string root, JsonElement materialFacts, string acceptedScope, IReadOnlyList<EvidenceReference> evidence,
        AgentRuntimeContext context, AgentLlmSelection model, Microsoft.Extensions.AI.ChatOptions? options,
        CancellationToken ct)
    {
        var key = AgentOperatingStateKey.Create("work-resolution:v2", new { project, type, authority, root, materialFacts, evidence });
        var draft = await DurableWorkHandoff.GetOrCreateAsync(key + ":draft", "work.resolution-draft.v2", async () => {
            using var client = context.CreateChatClient(model);
            return await RequirementDecisions.AssessAsync(client, acceptedScope, materialFacts, options, ct);
        }, context, ct);
        var reference = await DurableWorkHandoff.GetOrCreateAsync(key, "work.resolution-reference.v2", async () => {
            var request = RequirementDecisions.Bind(draft, project, type, authority, evidence, null, key, materialFacts);
            var decision = await context.Platform.RequestDecisionAsync(request, ct);
            return new Reference(decision.Id);
        }, context, ct);
        return await DecisionContinuations.ReadCurrentAsync(context.Platform, reference.DecisionId, project, ct)
            ?? throw new InvalidOperationException("The work resolution decision is no longer available.");
    }
}
