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
;
