using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>
/// The organization-scoped employee identity assigned to an installed agent when it is hired.
/// This is distinct from the package identity used to authenticate the agent implementation.
/// </summary>
public sealed record AgentIdentity(
    string EmployeeId,
    string DisplayName,
    string? RoleId,
    string? RoleName,
    string? RoleDescription,
    IReadOnlyList<string> RoleResponsibilities,
    string? AuthorityLevel,
    string? ManagerEmployeeId,
    string? ManagerDisplayName)
{
    public AgentTeamContext? TeamContext { get; init; }
    public IReadOnlyList<PortfolioSupervisionAssignment> ManagedWorkstreams { get; init; } = [];
}

public sealed record AgentTeamContext(
    string TeamId,
    string TeamKey,
    string Name,
    long Revision,
    string LeadEmployeeId,
    string LeadDisplayName,
    IReadOnlyList<AgentTeammate> Members,
    IReadOnlyList<TeamRoleCoverage> RoleCoverage,
    int TotalMemberCount,
    bool HasMore);

public sealed record AgentTeammate(
    string EmployeeId,
    string DisplayName,
    string EmployeeType,
    string? CompanyRole,
    string? TeamRole,
    string RelationshipToCaller,
    string Presence)
{
    public Guid? AgentInstallationId { get; init; }
    public IReadOnlyList<string> EffectiveCapabilities { get; init; } = [];
    public string RuntimeEligibility { get; init; } = "Unknown";
    public bool IsAvailable { get; init; } = true;
    /// <summary>Manifest-declared high-level roles that this agent can fill.</summary>
    public IReadOnlyList<string> DeclaredRoleKeys { get; init; } = [];
    /// <summary>Optional manifest-declared strengths; these rank candidates but do not establish eligibility.</summary>
    public IReadOnlyList<string> SpecializationKeys { get; init; } = [];
}

public sealed record TeamRoleCoverage(string Role, int Count);

public sealed record TeamRosterRequest(int Page = 1, int PageSize = 50);

public sealed record TeamRosterResponse(AgentTeamContext? Team);

public sealed record TeamRosterV2Response(AgentTeamContext? Team, Guid? WorkstreamId);
