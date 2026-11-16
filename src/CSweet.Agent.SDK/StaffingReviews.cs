using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Judgment-bearing work description. Identities, capabilities and approval authority remain code-owned.</summary>
public sealed record StaffingWorkDescription(string Purpose, string Timing, string ReportingLine,
    string Accountability, string Engagement)
{
    public ResourceChangeRole Apply(ResourceChangeRole role, Guid requester, Guid manager)
    {
        if (new[] { Purpose, Timing, Accountability, Engagement }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Describe the work, activation timing, accountability and engagement.");
        var reportsTo = ReportingLine switch
        {
            "Requester" => requester,
            "Manager" => manager,
            _ => throw new ArgumentException("Select a named current reporting relationship: Requester or Manager.")
        };
        return role with
        {
            Purpose = $"{Purpose}\n\nAccountability: {Accountability}\n\nEngagement: {Engagement}",
            Timing = Timing, ReportsToOrganizationUserId = reportsTo, ReportsToRoleKey = null
        };
    }
}

/// <summary>Current-state staffing review discovery usable by any agent without role-specific message parsing.</summary>
public static class StaffingReviews
{
    public static ResourceChangeRequestResponse? Latest(IEnumerable<ResourceChangeRequestResponse> requests,
        Guid requester, Guid manager, Guid team, Guid? project, Func<ResourceChangeRequestResponse, bool>? applies = null) =>
        requests.Where(r => r.RequesterOrganizationUserId == requester && r.ManagerOrganizationUserId == manager &&
                r.TeamId == team && r.WorkstreamId == project && applies?.Invoke(r) != false)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).FirstOrDefault();

    public static bool NeedsRevision(ResourceChangeRequestResponse? request) =>
        request?.Status is "RevisionRequested" or "Rejected";

    public static PersonalTodoDependency Dependency(ResourceChangeRequestResponse request) =>
        new(PersonalTodoDependencyKinds.ResourceChange, request.Id, request.Status);
}
