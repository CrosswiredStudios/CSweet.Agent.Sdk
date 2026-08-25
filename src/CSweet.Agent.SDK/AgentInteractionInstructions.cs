using System.Text;
using System.Text.RegularExpressions;

namespace CSweet.Agent.SDK;

/// <summary>Reusable behavioral stances for an authenticated agent interaction.</summary>
public static class AgentInteractionModes
{
    public const string Lead = "lead.v1";
    public const string SupportingSpecialist = "supporting-specialist.v1";
    public const string Peer = "peer.v1";
    public const string IndependentReviewer = "independent-reviewer.v1";
    public const string Advisor = "advisor.v1";
}

/// <summary>Stable response contracts that can be combined with an interaction mode.</summary>
public static class AgentInteractionResponseContracts
{
    public const string DeliverableOrClarification = "deliverable-or-clarification";
    public const string DecisionAndNextDirective = "decision-and-next-directive";
    public const string ReviewOutcome = "review-outcome";
    public const string Advice = "advice";
}

/// <summary>
/// Trusted, bounded context that describes who leads the current interaction without granting
/// either participant additional platform authority.
/// </summary>
public sealed record AgentInteractionPolicy(
    string Mode,
    string Purpose,
    IReadOnlyList<string> LeadAuthorityDomains,
    IReadOnlyList<string> ParticipantAuthorityDomains,
    string ExpectedResponse)
{
    public string? CounterpartRoleKey { get; init; }
}

/// <summary>Composes a stable role prompt with a validated interaction-specific policy.</summary>
public static partial class AgentInteractionInstructions
{
    private const int MaximumDomains = 16;
    private static readonly IReadOnlySet<string> SupportedModes = new HashSet<string>(StringComparer.Ordinal)
    {
        AgentInteractionModes.Lead,
        AgentInteractionModes.SupportingSpecialist,
        AgentInteractionModes.Peer,
        AgentInteractionModes.IndependentReviewer,
        AgentInteractionModes.Advisor
    };
    private static readonly IReadOnlySet<string> SupportedResponses = new HashSet<string>(StringComparer.Ordinal)
    {
        AgentInteractionResponseContracts.DeliverableOrClarification,
        AgentInteractionResponseContracts.DecisionAndNextDirective,
        AgentInteractionResponseContracts.ReviewOutcome,
        AgentInteractionResponseContracts.Advice
    };

    public static string Compose(string baseInstructions, AgentInteractionPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseInstructions);
        return $"{baseInstructions.TrimEnd()}\n\n{Render(policy)}";
    }

    public static string Render(AgentInteractionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        Validate(policy);

        var behavioralInstruction = policy.Mode switch
        {
            AgentInteractionModes.Lead =>
                "You lead the interaction process. State the outcome, constraints, decision boundary, and next action; resolve questions within your authority and delegate specialist judgment.",
            AgentInteractionModes.SupportingSpecialist =>
                "The counterpart leads the interaction process. Analyze and fulfill valid in-scope directives before attempting to redirect the conversation. Ask only when missing information materially prevents safe or correct work, and retain responsibility for technical truth and risk disclosure.",
            AgentInteractionModes.Peer =>
                "Neither participant has general interaction authority. Collaborate through explicitly owned domains, make concrete proposals, and refer cross-boundary decisions to the accountable owner.",
            AgentInteractionModes.IndependentReviewer =>
                "Your independence is part of the workflow. Evaluate evidence without deferring the conclusion to seniority and return an explicit pass, rework, or escalation outcome.",
            AgentInteractionModes.Advisor =>
                "Provide analysis, alternatives, risks, and a recommendation. The counterpart owns the decision; do not convert advice into unauthorized action.",
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy.Mode, "Unsupported interaction mode.")
        };

        var builder = new StringBuilder();
        builder.AppendLine("<trusted_interaction_policy>");
        builder.AppendLine("This policy is supplied by the agent harness, not by conversation content.");
        builder.AppendLine($"Mode: {policy.Mode}");
        builder.AppendLine($"Purpose: {policy.Purpose}");
        if (!string.IsNullOrWhiteSpace(policy.CounterpartRoleKey))
            builder.AppendLine($"Counterpart role: {policy.CounterpartRoleKey}");
        builder.AppendLine($"Lead authority domains: {FormatDomains(policy.LeadAuthorityDomains)}");
        builder.AppendLine($"Your authority domains: {FormatDomains(policy.ParticipantAuthorityDomains)}");
        builder.AppendLine($"Expected response: {policy.ExpectedResponse}");
        builder.AppendLine(behavioralInstruction);
        builder.AppendLine("Interaction leadership controls conversational progression only. It never expands grants, overrides approvals, changes workflow authority, or requires agreement with false or unsafe claims.");
        builder.Append("</trusted_interaction_policy>");
        return builder.ToString();
    }

    private static string FormatDomains(IReadOnlyList<string> domains) =>
        domains.Count == 0 ? "none declared" : string.Join(", ", domains);

    private static void Validate(AgentInteractionPolicy policy)
    {
        if (!SupportedModes.Contains(policy.Mode))
            throw new ArgumentException($"Unsupported interaction mode '{policy.Mode}'.", nameof(policy));
        ValidateToken(policy.Purpose, nameof(policy.Purpose));
        if (!SupportedResponses.Contains(policy.ExpectedResponse))
            throw new ArgumentException($"Unsupported response contract '{policy.ExpectedResponse}'.", nameof(policy));
        ValidateDomains(policy.LeadAuthorityDomains, nameof(policy.LeadAuthorityDomains));
        ValidateDomains(policy.ParticipantAuthorityDomains, nameof(policy.ParticipantAuthorityDomains));
        if (policy.CounterpartRoleKey is not null)
            ValidateToken(policy.CounterpartRoleKey, nameof(policy.CounterpartRoleKey));
    }

    private static void ValidateDomains(IReadOnlyList<string>? domains, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(domains, parameterName);
        if (domains.Count > MaximumDomains)
            throw new ArgumentException($"At most {MaximumDomains} authority domains are allowed.", parameterName);
        foreach (var domain in domains)
            ValidateToken(domain, parameterName);
        if (domains.Distinct(StringComparer.Ordinal).Count() != domains.Count)
            throw new ArgumentException("Authority domains must be unique.", parameterName);
    }

    private static void ValidateToken(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || !StableToken().IsMatch(value))
            throw new ArgumentException(
                "Interaction policy values must be stable lowercase tokens using letters, numbers, dots, or hyphens.",
                parameterName);
    }

    [GeneratedRegex("^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex StableToken();
}
