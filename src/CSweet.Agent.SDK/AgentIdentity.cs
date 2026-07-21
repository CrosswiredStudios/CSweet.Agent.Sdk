using CSweet.Agent.Contracts.Grpc;

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
    public static AgentIdentity? FromRegistration(RegistrationResult registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (registration.EmployeeIdentity is null ||
            string.IsNullOrWhiteSpace(registration.EmployeeIdentity.EmployeeId) ||
            string.IsNullOrWhiteSpace(registration.EmployeeIdentity.DisplayName))
        {
            return null;
        }

        var identity = registration.EmployeeIdentity;
        return new AgentIdentity(
            identity.EmployeeId,
            identity.DisplayName,
            NullIfWhiteSpace(identity.RoleId),
            NullIfWhiteSpace(identity.RoleName),
            NullIfWhiteSpace(identity.RoleDescription),
            identity.RoleResponsibilities.ToList(),
            NullIfWhiteSpace(identity.AuthorityLevel),
            NullIfWhiteSpace(identity.ManagerEmployeeId),
            NullIfWhiteSpace(identity.ManagerDisplayName));
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
