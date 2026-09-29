using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>Bounded incident handling independent of a manager's planning or creative intake state.</summary>
public static class ProjectIncidentReview
{
    public static async Task HandleManagerAsync(AgentEventEnvelope message, AgentRuntimeContext context,
        Func<ManagerIncidentContext, AgentRuntimeContext, CancellationToken, Task<ManagerIncidentAssessment>> assess, CancellationToken token)
    {
        var hint = message.Data.Deserialize<ProjectHealthHint>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (hint is null) return;
        var page = await context.Platform.ProjectHealth.ReadIncidentsAsync(new(IncidentId: hint.IncidentId), token);
        foreach (var incident in page.Incidents) await ReviewManagerAsync(incident, context, assess, token);
    }

    public static async Task RecoverManagerAsync(AgentRuntimeContext context,
        Func<ManagerIncidentContext, AgentRuntimeContext, CancellationToken, Task<ManagerIncidentAssessment>> assess, CancellationToken token)
    {
        try
        {
            var page = await context.Platform.ProjectHealth.ReadIncidentsAsync(new(Limit: 25), token);
            foreach (var incident in page.Incidents)
            {
                try { await ReviewManagerAsync(incident, context, assess, token); }
                catch (PlatformCapabilityException) { /* One unavailable incident cannot starve the rest. */ }
            }
        }
        catch (PlatformCapabilityException) { /* Old grants do not prevent ordinary manager work. */ }
    }

    private static async Task ReviewManagerAsync(ManagementIncident incident, AgentRuntimeContext context,
        Func<ManagerIncidentContext, AgentRuntimeContext, CancellationToken, Task<ManagerIncidentAssessment>> assess, CancellationToken token)
    {
        if (incident.Status != "Open" || incident.ReviewAt is not null || incident.CurrentRecipientId.ToString("D") != context.Identity?.EmployeeId) return;
        // The authoritative discovery endpoint omits scheduled reviews until they become due.
        ProjectDiagnosticsPage diagnostics;
        try { diagnostics = await context.Platform.ProjectHealth.ReadDiagnosticsAsync(new(incident.Id, Limit: 100), token); }
        catch (PlatformCapabilityException) { diagnostics = new([], null, ["Detailed diagnostics are unavailable; the persisted baseline is retained."]); }
        ManagerIncidentAssessment assessment;
        try
        {
            assessment = await assess(new(incident, diagnostics, $"incident-action:{incident.Id:N}:{incident.CurrentRecipientId:N}"), context, token);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Do not transmit arbitrary exception contents. The platform retains correlated attempt evidence.
            diagnostics = diagnostics with { Limitations = [.. diagnostics.Limitations, $"Manager assessment failed ({error.GetType().Name}); forwarding the baseline."] };
            assessment = ManagerIncidentAssessment.Escalate("Manager assessment failed; management assistance is required.");
        }
        var report = Baseline(incident, diagnostics, $"project-health:{incident.Id:N}:{incident.Revision}:{incident.CurrentRecipientId:N}");
        await context.Platform.ProjectHealth.ReportAsync(report with
        {
            RecommendedAction = Bound(assessment.Reason),
            Disposition = assessment.Disposition, ReviewAt = assessment.ReviewAt, ActionReference = assessment.ActionReference
        }, token);
    }

    public static bool Handles(string eventType) => eventType is ProjectHealthEvents.ReviewDue or ProjectHealthEvents.IncidentChanged;

    public static async Task HandleAsync(AgentEventEnvelope message, AgentRuntimeContext context, string responsibility,
        bool diagnose, CancellationToken token)
    {
        var hint = message.Data.Deserialize<ProjectHealthHint>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (hint is null) return;
        var page = await context.Platform.ProjectHealth.ReadIncidentsAsync(new(IncidentId: hint.IncidentId), token);
        foreach (var incident in page.Incidents)
            await ReviewAsync(incident, context, responsibility, diagnose, token);
    }

    public static async Task RecoverAsync(AgentRuntimeContext context, string responsibility, bool diagnose, CancellationToken token)
    {
        // One bounded page per attention wake; forwarded incidents leave the recipient queue.
        try
        {
            var page = await context.Platform.ProjectHealth.ReadIncidentsAsync(new(Limit: 25), token);
            foreach (var incident in page.Incidents)
                if (incident.CurrentRecipientId.ToString("D") == context.Identity?.EmployeeId)
                    await ReviewAsync(incident, context, responsibility, diagnose, token);
        }
        catch (PlatformCapabilityException) { /* Old/revoked grants do not break ordinary management work. Platform deadlines remain authoritative. */ }
    }

    private static async Task ReviewAsync(ManagementIncident incident, AgentRuntimeContext context, string responsibility,
        bool diagnose, CancellationToken token)
    {
        if (incident.Status != "Open" || incident.CurrentRecipientId.ToString("D") != context.Identity?.EmployeeId) return;
        var key = $"project-health:{incident.Id:N}:{incident.Revision}:{incident.CurrentRecipientId:N}";
        if (!diagnose)
        {
            await context.Platform.ProjectHealth.ForwardAsync(new(incident.Id, incident.Revision, key,
                $"My responsibility is {responsibility}. This incident requires operational diagnosis or platform repair outside that responsibility. " +
                "I am preserving the Producer's findings and escalating to my manager; no repair or scope change was performed."), token);
            return;
        }
        ProjectDiagnosticsPage evidence;
        try { evidence = await context.Platform.ProjectHealth.ReadDiagnosticsAsync(new(incident.Id, Limit: 100), token); }
        catch (PlatformCapabilityException) { evidence = new([], null, ["Detailed diagnostics are unavailable; the persisted baseline is retained."]); }
        var report = Baseline(incident, evidence, key);
        await context.Platform.ProjectHealth.ReportAsync(report, token);
    }

    public static ReportManagementIncident Baseline(ManagementIncident incident, ProjectDiagnosticsPage evidence, string key)
    {
        var observed = incident.Facts + "\n" + string.Join("\n", evidence.Evidence.Take(12).Select(x => $"{x.Kind} {x.SourceId}: {x.Detail}"));
        var cause = observed.Contains("payload_invalid", StringComparison.OrdinalIgnoreCase) || observed.Contains("schema", StringComparison.OrdinalIgnoreCase) || observed.Contains("validation_failed", StringComparison.OrdinalIgnoreCase)
            ? "Recorded errors indicate a structured payload or schema validation failure. The incompatible field or producer must be confirmed from the cited diagnostic detail."
            : observed.Contains("runtime.transport", StringComparison.OrdinalIgnoreCase)
            ? "Recorded errors indicate a communication transport failure; the underlying connection cause is unconfirmed."
            : observed.Contains("denied", StringComparison.OrdinalIgnoreCase)
            ? "Recorded errors indicate a capability or authorization failure; compare the attempted operation with the currently approved grants."
            : "The project has stopped making expected progress. Available evidence does not establish a root cause.";
        return new(incident.Id, incident.Revision, key, Bound(observed), cause,
            Bound(string.Join(" ", evidence.Limitations.Append(incident.MissingEvidence).Append(evidence.NextOffset.HasValue ? "Further diagnostic evidence exists beyond this bounded report." : ""))),
            "Review the cited failure, repair the responsible platform/agent integration or supply the missing dependency, then use the existing authorized recovery workflow. No new automatic repair was attempted.");
    }

    private static string Bound(string value) => value[..Math.Min(value.Length, 8000)];
}
