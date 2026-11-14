using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Review requirements for a planned deliverable. Managers can consult QA earlier when a ticket declares verification work.</summary>
public static class PlannedDeliveryReview
{
    public static bool DeliversCode(WorkItem item, string authorRole) =>
        item.Delivery?.DeliveryKind != "Artifact" && item.Planning?.DeliveryKind != "Artifact" &&
        RoleTaxonomy.CoreRoleKey(authorRole) == "software-developer";

    public static bool NeedsReadinessConsult(WorkItem item) =>
        item.Planning?.Verifications is { Count: > 0 } ||
        item.Planning?.DelegationRecommendations.Any(d => d.StageKey == "quality" ||
            RoleTaxonomy.CoreRoleKey(d.RequiredRoleKey) == "software-qa") == true;

    public static bool NeedsSpecialist(WorkItem item, string authorRole) =>
        DeliversCode(item, authorRole) || NeedsReadinessConsult(item);
}
