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
    /// <summary>
    /// The single project (workstream) this agent is currently assigned to, or
    /// <see langword="null"/> when unassigned. The platform guarantees at most one
    /// active project assignment per agent; use <see cref="ManagedWorkstreams"/>
    /// for supervision, not membership.
    /// </summary>
    public AssignedProjectContext? AssignedProject { get; init; }
    public IReadOnlyList<PortfolioSupervisionAssignment> ManagedWorkstreams { get; init; } = [];
}

/// <summary>
/// Snapshot of the single project assignment for an agent employee.
/// A <see langword="null"/> snapshot means the agent is not assigned to a project.
/// </summary>
public sealed record AssignedProjectContext(
    Guid WorkstreamId,
    string ProjectName,
    Guid? TeamId,
    Guid? BoardId,
    string? Role,
    long Revision,
    DateTimeOffset AssignedAt);

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

/// <summary>Response for the caller's single project assignment read; null means unassigned.</summary>
public sealed record ProjectAssignmentResponse(AssignedProjectContext? Assignment);
