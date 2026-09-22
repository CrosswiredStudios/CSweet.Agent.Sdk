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

    /// <summary>Return the stable core role behind a domain-specific role label.</summary>
    public static string CoreRoleKey(string roleKey) => roleKey switch
    {
        "game-engineer" => "software-developer",
        "game-quality-assurance" => "software-qa",
        _ => roleKey
    };

    /// <summary>Match a requested role against declared roles in the same core family.</summary>
    public static bool SatisfiesRole(IEnumerable<string?> declaredRoleKeys, string requiredRoleKey) =>
        IsCanonicalKey(requiredRoleKey) && declaredRoleKeys.Any(declared =>
            IsCanonicalKey(declared) &&
            string.Equals(CoreRoleKey(declared!), CoreRoleKey(requiredRoleKey), StringComparison.Ordinal));

    public static bool CanFill(ResourceChangeRole role, AgentTeammate teammate) =>
        IsCanonicalKey(role.RoleCategoryKey) &&
        SatisfiesRole(teammate.DeclaredRoleKeys, role.RoleCategoryKey);

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
            !SatisfiesRole(teammate.DeclaredRoleKeys, requirements.RequiredRoleKey))
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
