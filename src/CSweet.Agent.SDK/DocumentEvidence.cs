using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

public sealed record DocumentSource(Guid ArtifactId, Guid RevisionId, string Sha256, string Type, string Title, string Content);

/// <summary>The same authorized, exact source material is available to authors and independent reviewers.</summary>
public static class DocumentEvidence
{
    public static async Task<IReadOnlyList<DocumentSource>> ReadAsync(WorkItemPlanningSpecification planning,
        AgentRuntimeContext context, CancellationToken ct)
    {
        var result = new List<DocumentSource>();
        var members = planning.ArtifactPackageDigest?.Members ?? [];
        if (members.Count > 32) throw new InvalidOperationException("The source package exceeds the bounded document observation. Its owner must split the evidence package.");
        foreach (var member in members)
        {
            var document = await context.Platform.Artifacts.GetAsync(member.ArtifactId, ct);
            var revision = document.Revisions.SingleOrDefault(r => r.Id == member.AcceptedRevisionId);
            if (revision?.Status != "Accepted" || !Verify(revision.Content, member.Sha256) ||
                !string.Equals(revision.ContentSha256, member.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The exact accepted source revision is unavailable or changed.");
            result.Add(new(document.Id, revision.Id, revision.ContentSha256, document.DocumentType, document.Title, revision.Content));
        }
        return result;
    }

    public static bool Verify(string content, string sha) =>
        string.Equals(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content))), sha, StringComparison.OrdinalIgnoreCase);

    /// <summary>Current feedback is sufficient for rework; full prior history stays retrievable on the platform.</summary>
    public static object CurrentFeedback(IReadOnlyList<WorkExecutionOutcomeV1> outcomes)
    {
        var review = outcomes.LastOrDefault(o => o.OutcomeCode is "changes_requested" or "rejected" or "failed");
        return new { review?.Summary, review?.Diagnostics, review?.Output };
    }
}
