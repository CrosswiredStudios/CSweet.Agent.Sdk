namespace CSweet.Agent.SDK;

/// <summary>Distinguishes QA evidence artifacts from the product implementation being verified.</summary>
public static class DeliveryReviewIndependence
{
    public static bool IsQaEvidenceArtifact(string? deliveryKind, string? authorRole) =>
        deliveryKind == "Artifact" && authorRole is "software-qa" or "game-quality-assurance";
}

/// <summary>An actionable staffing gap, rather than a failed execution attempt.</summary>
public sealed class DeliveryStaffingException(string message) : InvalidOperationException(message);
