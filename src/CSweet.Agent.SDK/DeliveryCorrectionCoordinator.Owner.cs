using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSweet.WorkManagement.Contracts;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK;

// QFN Games, 2026-11-12: increment acceptance asked for a runnable test package that no task's scope covered.
// The manager model correctly answered NeedsPlanning, but the wait reason embedded its free-text rationale, so every
// write looked new, woke the manager again and asked the model again, about every 45 seconds. Nobody owned the
// next step either. A selection is now decided once per stable fingerprint: by the model when one task clearly owns
// the findings, otherwise by the owner, who chooses which task's owner fixes it or keeps the increment waiting.
public static partial class DeliveryCorrectionCoordinator
{
    /// <summary>Decision type for choosing which task owns a correction no accepted scope covers.</summary>
    public const string SourceDecisionType = "work-delivery.correction-source.v1";
    /// <summary>Option that leaves the increment waiting without corrective work.</summary>
    public const string KeepWaitingOption = "keep-waiting";
    private const string SourceOptionPrefix = "source:";
    private const int MaximumSourceOptions = 5;
    private const string NeedsPlanningPrefix = "Corrective source selection needs manager planning";

    internal sealed record SourceDecisionBinding(Guid PlanId, Guid CorrectionId, string Fingerprint);

    /// <summary>
    /// A stable identity for one selection: the correction, its findings and the exact source revisions.
    /// Model prose never takes part, so an unchanged situation is recognised as unchanged.
    /// </summary>
    internal static string SelectionFingerprint(WorkDeliveryCorrection correction, IEnumerable<WorkItem> sources) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            new[] { correction.Id.ToString("N") }
                .Concat(correction.FindingIds.Order().Select(x => x.ToString("N")))
                .Concat(sources.OrderBy(x => x.Id).Select(x => $"{x.Id:N}:{x.Revision}:{x.PlanningRevision}"))))))[..16];

    internal static string SelectionMarker(string fingerprint) => $"[selection {fingerprint}]";

    private static async Task<(WorkItem Source, string Reason)> SelectOrAskOwnerAsync(WorkDeliveryPlanResponse plan,
        WorkDeliveryCorrection correction, WorkItem[] sources, Func<IChatClient>? createPlanningClient,
        AgentRuntimeContext context, CancellationToken ct)
    {
        var fingerprint = SelectionFingerprint(correction, sources);
        var marker = SelectionMarker(fingerprint);
        var decision = await ReadSourceDecisionAsync(plan, correction, fingerprint, context, ct);
        if (decision is { Status: DecisionStatuses.Decided } &&
            sources.FirstOrDefault(s => decision.SelectedOptionId == SourceOptionPrefix + s.Id.ToString("N")) is { } chosen)
            return (chosen, $"The owner assigned this correction to '{chosen.Title}' (decision {decision.Id:D}).");
        if (decision is not null) throw new InvalidOperationException(OwnerWait(decision, marker));
        // The model already answered NeedsPlanning for exactly this situation; asking again cannot change it.
        if (correction.WaitingReason?.Contains(marker, StringComparison.Ordinal) == true)
            throw new InvalidOperationException(correction.WaitingReason);
        string needs;
        if (createPlanningClient is null) needs = $"{NeedsPlanningPrefix}: several completed tasks could own the findings and no planning model is configured.";
        else
        {
            try { return await SelectSourceAsync(plan, correction, sources, createPlanningClient, ct); }
            catch (InvalidOperationException error) when (error.Message.StartsWith(NeedsPlanningPrefix, StringComparison.Ordinal))
            { needs = error.Message; }
        }
        var requested = await RequestSourceDecisionAsync(plan, correction, sources, fingerprint, needs, context, ct);
        throw new InvalidOperationException(requested is null ? $"{needs} {marker}" : OwnerWait(requested, marker));
    }

    private static string OwnerWait(DecisionRecord decision, string marker) => decision.Status == DecisionStatuses.Pending
        ? $"Waiting for the owner to choose which task fixes the acceptance findings (decision {decision.Id:D}). {marker}"
        : $"The owner chose to keep this increment waiting (decision {decision.Id:D}). {marker}";

    internal static SourceDecisionBinding? SourceDecisionOf(DecisionRecord decision)
    {
        if (decision.TypeKey != SourceDecisionType || decision.TypeData is not { ValueKind: JsonValueKind.Object } data ||
            !data.TryGetProperty("correctionSource", out var value)) return null;
        try { return value.Deserialize<SourceDecisionBinding>(SelectionJson); }
        catch (JsonException) { return null; }
    }

    private static async Task<DecisionRecord?> ReadSourceDecisionAsync(WorkDeliveryPlanResponse plan, WorkDeliveryCorrection correction,
        string fingerprint, AgentRuntimeContext context, CancellationToken ct)
    {
        try
        {
            return (await context.Platform.ReadDecisionsAsync(new(WorkstreamId: plan.WorkstreamId), ct))
                .Where(x => SourceDecisionOf(x) is { } binding && binding.CorrectionId == correction.Id && binding.Fingerprint == fingerprint)
                .OrderBy(x => x.CreatedAt).LastOrDefault();
        }
        catch (PlatformCapabilityException)
        {
            // Without decision access the stable wait marker still prevents repeat model calls.
            return null;
        }
    }

    private static async Task<DecisionRecord?> RequestSourceDecisionAsync(WorkDeliveryPlanResponse plan, WorkDeliveryCorrection correction,
        WorkItem[] sources, string fingerprint, string needs, AgentRuntimeContext context, CancellationToken ct)
    {
        var findings = plan.Findings.Where(f => correction.FindingIds.Contains(f.Id)).Select(f => f.Summary).ToArray();
        var listed = string.Join(" ", findings.Take(3).Select(f => "• " + (f.Length > 300 ? f[..300] + "…" : f)));
        var reason = needs.StartsWith(NeedsPlanningPrefix + ":", StringComparison.Ordinal) ? needs[(NeedsPlanningPrefix.Length + 1)..].Trim() : needs;
        var summary = $"Increment acceptance found a problem that no completed task's accepted scope covers, so it can't be assigned automatically. {listed} " +
            $"Why: {reason} Choose which task's owner fixes it (a corrective task is added and the increment is accepted again), or keep the increment waiting.";
        var options = sources.Take(MaximumSourceOptions).Select(s => new DecisionOption(SourceOptionPrefix + s.Id.ToString("N"),
                Truncate($"Fix it in \"{s.Title}\"", 120), "Adds a corrective task for this task's owner on its story branch, then re-runs acceptance."))
            .Append(new DecisionOption(KeepWaitingOption, "Keep the increment waiting", "No corrective work is created; the next Story stays unplanned."))
            .ToArray();
        try
        {
            return await context.Platform.RequestDecisionAsync(new(plan.WorkstreamId, SourceDecisionType, Truncate(summary, 3000),
                "material-strategy-change", options, KeepWaitingOption, [], null,
                "Acceptance of this increment and planning of the next Story wait for this choice.", null,
                $"delivery-correction-source:{correction.Id:N}:{fingerprint}",
                JsonSerializer.SerializeToElement(new { correctionSource = new SourceDecisionBinding(plan.Id, correction.Id, fingerprint) }, SelectionJson)), ct);
        }
        catch (PlatformCapabilityException)
        {
            // Without decision access the wait still records the stable marker, so the model is not asked again.
            return null;
        }
    }

    // A build-readiness finding that only says "no runnable test package" is not a defect when every Code task in the
    // increment declared no browser evidence. Ask the platform to re-run acceptance instead of planning a repair.
    private static async Task<bool> RecoverNoRunnableOutputAsync(WorkDeliveryPlanResponse plan, WorkDeliveryCorrection correction,
        WorkDeliveryExecutionResponse rejected, AgentRuntimeContext context, CancellationToken ct)
    {
        var findings = plan.Findings.Where(f => correction.FindingIds.Contains(f.Id)).ToArray();
        if (rejected.Scope != WorkExecutionScopes.Release || findings.Length == 0 ||
            findings.Any(f => f.Summary != DeliveryComputeTest.NoRunnablePackage) ||
            await DeliveryComputeTest.RunnableOutputInScopeAsync(plan, rejected.Scope, rejected.ItemId, context, ct)) return false;
        try
        {
            await context.Platform.Work.RecoverDeliveryAsync(new(plan.Id, rejected.Id, rejected.Revision,
                $"delivery-correction:{correction.Id:N}:no-runnable-output:{rejected.Revision}",
                "Build readiness asked for a runnable test package, but every Code task in this increment declares no browser evidence, " +
                "so there is no application to start. Re-run acceptance; build readiness records that nothing runnable is in scope."), ct);
            return true;
        }
        catch (PlatformCapabilityException)
        {
            // An older platform cannot supersede this finding; fall back to ordinary corrective planning.
            return false;
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
}
