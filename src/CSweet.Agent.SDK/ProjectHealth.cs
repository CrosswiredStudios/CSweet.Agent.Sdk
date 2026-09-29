namespace CSweet.Agent.SDK;

/// <summary>Read-only diagnosis and durable management escalation; never grants repair authority.</summary>
public static class ProjectHealthCapabilities
{
    public const string Read = CapabilityNames.ProjectHealth.Read;
    public const string Diagnostics = CapabilityNames.ProjectHealth.Diagnostics;
    public const string Incidents = CapabilityNames.ProjectHealth.Incidents;
    public const string Report = CapabilityNames.ProjectHealth.Report;
    public const string Forward = CapabilityNames.ProjectHealth.Forward;
}

public static class ProjectHealthEvents
{
    public const string ReviewDue = "com.csweet.project-health.review-due.v1";
    public const string IncidentChanged = "com.csweet.management.incident.changed.v1";
}

public sealed record ProjectHealthHint(Guid WorkstreamId, Guid IncidentId, long Revision);
public sealed record ReadProjectHealth(Guid WorkstreamId);
public sealed record ReadProjectDiagnostics(Guid IncidentId, int Offset = 0, int Limit = 50);
public sealed record ReadManagementIncidents(Guid? IncidentId = null, Guid? WorkstreamId = null, int Offset = 0, int Limit = 50);
public sealed record ProjectHealthSnapshot(Guid WorkstreamId, DateTimeOffset LastProgressAt, DateTimeOffset NextReviewAt,
    bool HasActiveWork, string? WaitingReason, IReadOnlyList<Guid> OpenIncidentIds);
public sealed record ProjectDiagnosticEvidence(string Kind, Guid SourceId, DateTimeOffset OccurredAt, string Detail,
    string? Link = null);
public sealed record ProjectDiagnosticsPage(IReadOnlyList<ProjectDiagnosticEvidence> Evidence, int? NextOffset,
    IReadOnlyList<string> Limitations);
public sealed record ManagementIncidentHop(Guid FromEmployeeId, Guid? ToEmployeeId, DateTimeOffset OccurredAt, string Reason);
public sealed record ManagementIncident(Guid Id, Guid WorkstreamId, Guid ProducerEmployeeId, Guid CurrentRecipientId,
    string Status, string Reason, Guid? AffectedWorkItemId, DateTimeOffset DetectedAt, DateTimeOffset LastProgressAt,
    DateTimeOffset? EscalateAt, long Revision, string Facts, string LikelyCause, string MissingEvidence,
    string RecommendedAction, IReadOnlyList<ManagementIncidentHop> History)
{
    /// <summary>Generic monitoring owner. ProducerEmployeeId remains on the wire for older clients.</summary>
    public Guid MonitoringManagerEmployeeId => ProducerEmployeeId;
    public string Disposition { get; init; } = IncidentDispositions.Escalate;
    public DateTimeOffset? ReviewAt { get; init; }
    public string? ActionReference { get; init; }
}
public sealed record ManagementIncidentPage(IReadOnlyList<ManagementIncident> Incidents, int? NextOffset);
public sealed record ReportManagementIncident(Guid IncidentId, long ExpectedRevision, string IdempotencyKey,
    string Facts, string LikelyCause, string MissingEvidence, string RecommendedAction)
{
    public string Disposition { get; init; } = IncidentDispositions.Escalate;
    public DateTimeOffset? ReviewAt { get; init; }
    public string? ActionReference { get; init; }
}
public sealed record ForwardManagementIncident(Guid IncidentId, long ExpectedRevision, string IdempotencyKey, string Reason);

public sealed class PlatformProjectHealthClient(PlatformCapabilityClient platform)
{
    public Task<ProjectHealthSnapshot> ReadAsync(ReadProjectHealth request, CancellationToken token = default) =>
        platform.InvokeAsync<ReadProjectHealth, ProjectHealthSnapshot>(ProjectHealthCapabilities.Read, request, token);
    public Task<ProjectDiagnosticsPage> ReadDiagnosticsAsync(ReadProjectDiagnostics request, CancellationToken token = default) =>
        platform.InvokeAsync<ReadProjectDiagnostics, ProjectDiagnosticsPage>(ProjectHealthCapabilities.Diagnostics, request, token);
    public Task<ManagementIncidentPage> ReadIncidentsAsync(ReadManagementIncidents request, CancellationToken token = default) =>
        platform.InvokeAsync<ReadManagementIncidents, ManagementIncidentPage>(ProjectHealthCapabilities.Incidents, request, token);
    public Task<ManagementIncident> ReportAsync(ReportManagementIncident request, CancellationToken token = default) =>
        platform.InvokeAsync<ReportManagementIncident, ManagementIncident>(ProjectHealthCapabilities.Report, request, token);
    public Task<ManagementIncident> ForwardAsync(ForwardManagementIncident request, CancellationToken token = default) =>
        platform.InvokeAsync<ForwardManagementIncident, ManagementIncident>(ProjectHealthCapabilities.Forward, request, token);
}
