using System.Security.Cryptography;
using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Current planning facts, not an execution grant or a requirement to acquire a fixed roster.</summary>
public sealed record PlanningResourceContext(Guid WorkstreamId, Guid TeamId, AgentTeamContext? Roster,
    IReadOnlyList<ResourceChangeRequestResponse> Staffing, IReadOnlyList<ResourceRequestState> Resources)
{
    /// <summary>Only substantive state changes invalidate a planning judgment; timestamps and prose do not.</summary>
    public string Fingerprint => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
        WorkstreamId, TeamId, roster = Roster is null ? null : new { Roster.Revision, Roster.LeadEmployeeId,
            members = Roster.Members.OrderBy(m => m.EmployeeId).Select(m => new { m.EmployeeId, m.AgentInstallationId,
                m.IsAvailable, m.RuntimeEligibility, roles = m.DeclaredRoleKeys.Order(), capabilities = m.EffectiveCapabilities.Order() }) },
        staffing = Staffing.OrderBy(s => s.Id).Select(s => new { s.Id, s.Status, s.DeliveryStatus, s.DecidedByOrganizationUserId,
            s.ManagerOrganizationUserId, s.ContextRevision, s.TeamId, s.WorkstreamId,
            roles = s.Roles.OrderBy(r => r.RoleKey).Select(r => new { r.RoleKey, r.RoleCategoryKey, r.Headcount, r.TeamId,
                capabilities = r.RequiredCapabilities.Order() }) }),
        resources = Resources.OrderBy(r => r.Id).Select(r => new { r.Id, r.Revision, r.Status, r.PlanHash,
            r.ApproverId, r.ProviderInstallationId, r.ResourceType, r.ProfileKey })
    })));

    public static async Task<PlanningResourceContext> ReadAsync(PlatformCapabilityClient platform,
        Guid workstream, Guid team, CancellationToken ct = default)
    {
        AgentTeamContext? roster = null;
        var members = new List<AgentTeammate>();
        for (var page = 1; ; page++)
        {
            var response = await platform.ReadTeamRosterAsync(new TeamRosterV2Request(team, workstream, page, 100), ct);
            var current = response.Team;
            if (current is null) { roster = null; break; }
            if (current.TeamId != team.ToString() || response.WorkstreamId is { } actual && actual != workstream ||
                roster is not null && roster.Revision != current.Revision)
                throw new InvalidOperationException("Planning roster changed or belongs to a different scope; read the current project again.");
            roster ??= current;
            members.AddRange(current.Members);
            if (!current.HasMore) { roster = roster with { Members = members.DistinctBy(m => m.EmployeeId).ToArray(), HasMore = false }; break; }
            if (current.Members.Count == 0 || page * 100 >= current.TotalMemberCount)
                throw new InvalidOperationException("Planning roster paging is inconsistent; no complete staffing snapshot is available.");
        }
        var staffing = await platform.ReadResourceChangeContextAsync(ct);
        var resources = await platform.Resources.ReadAsync(new(WorkstreamId: workstream), ct);
        return new(workstream, team, roster,
            staffing.Requests.Where(s => s.WorkstreamId == workstream || s.WorkstreamId is null &&
                (s.TeamId == team || s.Roles.Any(r => r.TeamId == team))).ToArray(),
            resources.Where(r => r.WorkstreamId == workstream).ToArray());
    }
}
