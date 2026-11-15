using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

public sealed class PlatformWorkIssueClient(PlatformCapabilityClient platform)
{
    public Task<WorkIssueDirectory> ReadAsync(ReadWorkIssuesRequest request, CancellationToken ct = default) =>
        Invoke<ReadWorkIssuesRequest, WorkIssueDirectory>(WorkIssueCapabilities.Read, request, () => WorkIssueValidation.Validate(request), ct);
    public Task<WorkIssueDirectory> ReconcileAsync(ReconcileWorkIssuesRequest request, CancellationToken ct = default) =>
        Invoke<ReconcileWorkIssuesRequest, WorkIssueDirectory>(WorkIssueCapabilities.Reconcile, request, () => WorkIssueValidation.Validate(request), ct);
    public Task<WorkIssueCommissionResponse> CommissionAsync(CommissionWorkIssuesRequest request, CancellationToken ct = default) =>
        Invoke<CommissionWorkIssuesRequest, WorkIssueCommissionResponse>(WorkIssueCapabilities.Commission, request, () => WorkIssueValidation.Validate(request), ct);
    /// <summary>Retires idle tracking after a source-bound terminal receipt. Failed attempts and actual tasks retain their history.</summary>
    public async Task RetireTrackingAsync(WorkIssueCaseResponse issue, Guid ownerEmployeeId, CancellationToken ct = default)
    {
        if (issue.Status is not ("Resolved" or "Superseded") || string.IsNullOrWhiteSpace(issue.ResolutionEvidence))
            throw new ArgumentException("A terminal issue receipt with evidence is required.");
        var sessions = issue.Links.Where(l => l.Kind == "Coordination").Select(l => l.Id).ToHashSet();
        var directory = await platform.PersonalTodo.ListAsync(ct);
        foreach (var task in directory.Boards.Where(b => b.OwnerOrganizationUserId == ownerEmployeeId).SelectMany(b => b.Items))
        {
            if (task.Status is not (PersonalTodoStatuses.Ready or PersonalTodoStatuses.Blocked) || !TrackedCollaboration.IsTrackingCard(task) ||
                task.WorkContext?.WorkstreamId != issue.WorkstreamId || task.WorkContext.CoordinationSessionId is not Guid session || !sessions.Contains(session)) continue;
            await platform.PersonalTodo.ArchiveAsync(new(task.Id, task.Revision,
                AgentOperatingStateKey.Create("issue-tracking-retired:v1", new { issueId = issue.Id, issue.Revision, taskId = task.Id })), ct);
        }
    }
    private Task<T> Invoke<TRequest,T>(string capability, TRequest request, Action validate, CancellationToken ct)
    {
        try { validate(); } catch (ArgumentException e) { throw new PlatformCapabilityException(capability, PlatformCapabilityErrorCode.ValidationFailed, e.Message, e); }
        return platform.InvokeAsync<TRequest,T>(capability, request, ct);
    }
    public async Task<WorkIssueDirectory> ReadAllAsync(Guid boardId, bool history = false, CancellationToken ct = default)
    {
        var issues = new List<WorkIssueCaseResponse>(); var commissions = new Dictionary<Guid, WorkIssueCommissionResponse>();
        for (int page = 1; ; page++)
        {
            var current = await ReadAsync(new(boardId, history, page, 100), ct);
            issues.AddRange(current.Issues); foreach (var c in current.Commissions) commissions[c.Id] = c;
            if (!current.HasMore) return new(issues, commissions.Values.ToArray());
            ct.ThrowIfCancellationRequested(); // Authorized pages may contain only sources hidden from this reader.
        }
    }
}
