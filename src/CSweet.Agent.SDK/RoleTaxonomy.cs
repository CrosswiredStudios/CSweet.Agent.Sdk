using System.Text.RegularExpressions;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Shared matching rules for stable role categories and optional specializations.</summary>
public static partial class RoleTaxonomy
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalKeyPattern();

    public static bool IsCanonicalKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 160 && CanonicalKeyPattern().IsMatch(value);

    public static bool CanFill(ResourceChangeRole role, AgentTeammate teammate) =>
        IsCanonicalKey(role.RoleCategoryKey) &&
        teammate.DeclaredRoleKeys.Contains(role.RoleCategoryKey, StringComparer.Ordinal);

    public static int PreferredSpecializationScore(ResourceChangeRole role, AgentTeammate teammate) =>
        role.PreferredSpecializationKeys.Count == 0
            ? 0
            : role.PreferredSpecializationKeys.Count(key =>
                teammate.SpecializationKeys.Contains(key, StringComparer.Ordinal));

    public static bool IsEligible(
        AgentTeammate teammate,
        WorkAssignmentRequirements requirements)
    {
        if (!teammate.IsAvailable || teammate.AgentInstallationId is null ||
            !string.Equals(teammate.RuntimeEligibility, "Eligible", StringComparison.OrdinalIgnoreCase) ||
            !teammate.DeclaredRoleKeys.Contains(requirements.RequiredRoleKey, StringComparer.Ordinal))
            return false;

        return requirements.RequiredSpecializationKeys.All(key =>
                   teammate.SpecializationKeys.Contains(key, StringComparer.Ordinal)) &&
               requirements.RequiredCapabilityKeys.All(key =>
                   teammate.EffectiveCapabilities.Contains(key, StringComparer.Ordinal));
    }

    public static AgentAssignmentCandidate? SelectAssignment(
        IEnumerable<AgentTeammate> teammates,
        WorkAssignmentRequirements requirements,
        IReadOnlyDictionary<Guid, int>? currentWipByInstallation = null)
    {
        return teammates
            .Where(teammate => IsEligible(teammate, requirements))
            .Select(teammate => new AgentAssignmentCandidate(
                teammate,
                requirements.PreferredSpecializationKeys.Count(key =>
                    teammate.SpecializationKeys.Contains(key, StringComparer.Ordinal)),
                currentWipByInstallation?.GetValueOrDefault(teammate.AgentInstallationId!.Value) ?? 0))
            .OrderByDescending(candidate => candidate.PreferredSpecializationCount)
            .ThenBy(candidate => candidate.CurrentWip)
            .ThenBy(candidate => candidate.Teammate.AgentInstallationId)
            .FirstOrDefault();
    }
}

public sealed record AgentAssignmentCandidate(
    AgentTeammate Teammate,
    int PreferredSpecializationCount,
    int CurrentWip);
