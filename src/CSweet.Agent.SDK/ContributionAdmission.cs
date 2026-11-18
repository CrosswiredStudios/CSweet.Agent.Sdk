using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Evidence a board manager presents to the authority that issued an earlier direction.</summary>
public sealed record ContributionAdmission(Guid BoardId, string StoryKey, Guid SessionId, string ArtifactDigest,
    Guid PriorDecisionId, long PriorRevision, Guid PriorAuthorityId, string OriginalDirection,
    IReadOnlyList<string> Deliverables)
{
    public const string Contract = "work.contribution-admission.v1";
    public const string Admit = "continue-current-plan";
    public const string Hold = "request-more-evidence";
    public string Kind => Contract;

    public static ContributionAdmission? Read(JsonElement? data)
    {
        if (data is not { ValueKind: JsonValueKind.Object } value || !value.TryGetProperty("kind", out var kind) ||
            kind.ValueKind != JsonValueKind.String || kind.GetString() != Contract) return null;
        return value.Deserialize<ContributionAdmission>(JsonSerializerOptions.Web);
    }

    public bool Allows(string status, string? selected, Guid? authority, string? rationale) =>
        status == DecisionStatuses.Decided && selected == Admit && authority == PriorAuthorityId &&
        !string.IsNullOrWhiteSpace(rationale);

    public bool Allows(DecisionRecord decision) => decision.SupersededByDecisionId is null &&
        Allows(decision.Status, decision.SelectedOptionId, decision.DecidedByOrganizationUserId, decision.Rationale);
}
