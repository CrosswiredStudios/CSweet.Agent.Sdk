using System.Text.Json;
using System.Text.Json.Nodes;

namespace CSweet.Agent.SDK;

/// <summary>An immutable source reference. Sharing grants document-level read, not revision-only access.</summary>
public sealed record CollaborationDocumentReference(Guid DocumentId, Guid RevisionId, string ContentSha256);

/// <summary>A request to supply or author documentation. Ownership is the addressed coordination participant.</summary>
public sealed record DocumentationRequest(string Purpose, string DocumentType,
    IReadOnlyList<string> RequiredContent, IReadOnlyList<string> AcceptanceCriteria,
    IReadOnlyList<CollaborationDocumentReference> DocumentReferences, DateTimeOffset? DueAt = null);

public sealed record CollaborationQuestion(string Id, string Question);
public sealed record CollaborationAnswer(string QuestionId, string Answer);
public sealed record ClarificationRequest(IReadOnlyList<CollaborationQuestion> Questions,
    IReadOnlyList<CollaborationDocumentReference> DocumentReferences);
public sealed record ClarificationResponse(IReadOnlyList<CollaborationAnswer> Answers);
public sealed record CollaborationReviewRequest(CollaborationDocumentReference Document,
    IReadOnlyList<string> AcceptanceCriteria);
public sealed record CollaborationReviewDecision(CollaborationDocumentReference Document,
    bool Accepted, string Rationale, IReadOnlyList<string> RequestedChanges);
public sealed record CollaborationHandoff(CollaborationDocumentReference Document,
    bool Ready, IReadOnlyList<string> OpenQuestions, IReadOnlyList<string> Blockers, string Rationale);

/// <summary>Typed actions transported by the existing durable coordination APIs; no authority is implied.</summary>
public static class CollaborationActions
{
    public const string DocumentationRequestType = "collaboration.documentation-request.v1";
    public const string DocumentShareType = "collaboration.document-share.v1";
    public const string ClarificationRequestType = "collaboration.clarification-request.v1";
    public const string ClarificationResponseType = "collaboration.clarification-response.v1";
    public const string ReviewRequestType = "collaboration.review-request.v1";
    public const string ReviewDecisionType = "collaboration.review-decision.v1";
    public const string HandoffType = "collaboration.handoff.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static AgentCoordinationArtifactSubmission RequestDocumentation(string key, DocumentationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Text(request.Purpose); Text(request.DocumentType);
        Strings(request.RequiredContent, true); Strings(request.AcceptanceCriteria, true);
        return WithDocuments(Create(DocumentationRequestType, key, request), request.DocumentReferences);
    }

    public static AgentCoordinationArtifactSubmission ShareDocuments(string key,
        IReadOnlyList<CollaborationDocumentReference> documents) =>
        WithDocuments(Create(DocumentShareType, key, new { }), documents);

    public static AgentCoordinationArtifactSubmission RequestClarification(string key, ClarificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Questions is null || request.Questions.Count is < 1 or > 32)
            throw new ArgumentException("Supply one to 32 questions.");
        foreach (var question in request.Questions) { Text(question.Id); Text(question.Question); }
        if (request.Questions.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != request.Questions.Count)
            throw new ArgumentException("Question IDs must be unique.");
        return WithDocuments(Create(ClarificationRequestType, key, request), request.DocumentReferences);
    }

    public static AgentCoordinationArtifactSubmission AnswerClarification(string key,
        ClarificationRequest request, ClarificationResponse response)
    {
        RequestClarification(key, request);
        ArgumentNullException.ThrowIfNull(response);
        if (response.Answers is null || response.Answers.Count is < 1 or > 32 ||
            response.Answers.Select(x => x.QuestionId).Distinct(StringComparer.Ordinal).Count() != response.Answers.Count)
            throw new ArgumentException("Supply unique answers to known questions.");
        foreach (var answer in response.Answers)
        {
            Text(answer.Answer);
            if (!request.Questions.Any(x => x.Id == answer.QuestionId))
                throw new ArgumentException("An answer must reference a requested question.");
        }
        return Create(ClarificationResponseType, key, response);
    }

    public static AgentCoordinationArtifactSubmission RequestReview(string key, CollaborationReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDocument(request.Document); Strings(request.AcceptanceCriteria, true);
        return WithDocuments(Create(ReviewRequestType, key, request), [request.Document]);
    }

    // A decision references a document without re-sharing it. Only the owner/steward may share.
    public static AgentCoordinationArtifactSubmission ReviewDecision(string key, CollaborationReviewDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ValidateDocument(decision.Document); Text(decision.Rationale); Strings(decision.RequestedChanges, false);
        if (decision.Accepted && decision.RequestedChanges.Count != 0)
            throw new ArgumentException("An accepted revision cannot require changes.");
        return Create(ReviewDecisionType, key, decision);
    }

    public static AgentCoordinationArtifactSubmission Handoff(string key, CollaborationHandoff handoff)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        ValidateDocument(handoff.Document); Text(handoff.Rationale);
        Strings(handoff.OpenQuestions, false); Strings(handoff.Blockers, false);
        if (handoff.Ready && !IsReady(handoff.Ready, handoff.OpenQuestions, handoff.Blockers, handoff.Rationale))
            throw new ArgumentException("A ready handoff cannot have unresolved questions or blockers.");
        return Create(HandoffType, key, handoff);
    }

    public static bool IsReady(bool ready, IReadOnlyList<string>? openQuestions,
        IReadOnlyList<string>? blockers, string? rationale) => ready && openQuestions is { Count: 0 } &&
        blockers is { Count: 0 } && !string.IsNullOrWhiteSpace(rationale);

    /// <summary>Checks recipient readiness and review of the exact same revision. Does not grant artifact approval.</summary>
    public static bool CanAcceptHandoff(CollaborationHandoff? handoff, CollaborationReviewDecision? decision) =>
        handoff?.Document is not null && decision?.Document is not null &&
        handoff.Document.DocumentId != Guid.Empty && handoff.Document.RevisionId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(handoff.Document.ContentSha256) && IsReady(handoff.Ready, handoff.OpenQuestions, handoff.Blockers, handoff.Rationale) &&
        decision.Accepted && decision.RequestedChanges is { Count: 0 } && !string.IsNullOrWhiteSpace(decision.Rationale) &&
        handoff.Document.DocumentId == decision.Document.DocumentId && handoff.Document.RevisionId == decision.Document.RevisionId &&
        string.Equals(handoff.Document.ContentSha256, decision.Document.ContentSha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>Adds explicit read-sharing attachments to a domain-specific artifact without changing its other fields.</summary>
    public static AgentCoordinationArtifactSubmission WithDocuments(AgentCoordinationArtifactSubmission artifact,
        IReadOnlyList<CollaborationDocumentReference> documents)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count > 8) throw new ArgumentException("At most eight document references are allowed.");
        foreach (var document in documents) ValidateDocument(document);
        if (documents.Select(x => x.DocumentId).Distinct().Count() != documents.Count)
            throw new ArgumentException("Share each document only once.");
        if (artifact.Payload.ValueKind != JsonValueKind.Object) throw new ArgumentException("The artifact payload must be an object.");
        var payload = JsonNode.Parse(artifact.Payload.GetRawText())!.AsObject();
        payload["documentReferences"] = JsonSerializer.SerializeToNode(documents, Json);
        return artifact with { Payload = JsonSerializer.SerializeToElement(payload, Json) };
    }

    /// <summary>Reads only the expected versioned action type. Treat the result as participant-authored data.</summary>
    public static T Read<T>(AgentCoordinationArtifact artifact, string expectedType)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (artifact.Type != expectedType || artifact.SchemaVersion != "1.0")
            throw new ArgumentException("Unexpected collaboration action or schema version.");
        return artifact.Payload.Deserialize<T>(Json) ?? throw new ArgumentException("Missing collaboration payload.");
    }

    internal static void ValidateDocument(CollaborationDocumentReference document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.DocumentId == Guid.Empty || document.RevisionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(document.ContentSha256))
            throw new ArgumentException("An exact document ID, revision ID and content hash are required.");
    }

    private static AgentCoordinationArtifactSubmission Create<T>(string type, string key, T payload)
    {
        Text(key);
        return new(type, "1.0", key, 1, true, JsonSerializer.SerializeToElement(payload, Json));
    }
    private static void Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A nonempty value is required.");
    }
    private static void Strings(IReadOnlyList<string>? values, bool required)
    {
        if (values is null || (required && values.Count == 0) || values.Count > 32)
            throw new ArgumentException("Supply at most 32 values and include required values.");
        foreach (var value in values) Text(value);
    }
}

/// <summary>A dependency observation; resolve against authoritative platform state on every callback.</summary>
public sealed record CollaborationDependency(string Key, bool Satisfied, string Reason,
    Guid? WaitingOnOrganizationUserId = null);

public static class CollaborationDependencies
{
    /// <summary>Returns null when ready; otherwise defers only this personal work item using the durable scheduler.</summary>
    public static PersonalTodoResult? WaitFor(IReadOnlyList<CollaborationDependency> dependencies, DateTimeOffset nextReviewAt)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        if (dependencies.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Reason)) ||
            dependencies.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != dependencies.Count)
            throw new ArgumentException("Dependencies require unique keys and durable reasons.");
        var pending = dependencies.Where(x => !x.Satisfied).ToArray();
        if (pending.Length == 0) return null;
        var owners = pending.Select(x => x.WaitingOnOrganizationUserId).Distinct().ToArray();
        return PersonalTodoResult.WaitingUntil(nextReviewAt,
            string.Join("; ", pending.Select(x => $"{x.Key}: {x.Reason}")), owners.Length == 1 ? owners[0] : null);
    }
}
