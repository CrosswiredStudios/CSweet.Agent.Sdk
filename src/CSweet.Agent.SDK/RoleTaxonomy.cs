using System.Text.RegularExpressions;

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
}
