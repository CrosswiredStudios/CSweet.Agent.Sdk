using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>Typed, grant-governed access to C-Sweet Markdown artifacts and document packages.</summary>
public sealed class PlatformArtifactClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPlatformToolInvoker _tools;
    internal PlatformArtifactClient(IPlatformToolInvoker tools) => _tools = tools;

    public Task<ArtifactDocument> CreateAsync(CreateArtifactDocument request, CancellationToken token = default) =>
        InvokeAsync<CreateArtifactDocument, ArtifactDocument>(PlatformCapabilities.ArtifactCreate, request, token);
    public Task<IReadOnlyList<ArtifactDocumentSummary>> ListAsync(bool includeArchived = false, CancellationToken token = default) =>
        InvokeAsync<object, IReadOnlyList<ArtifactDocumentSummary>>(PlatformCapabilities.ArtifactRead, new { artifactId = (Guid?)null, includeArchived }, token);
    public Task<ArtifactDocument> GetAsync(Guid artifactId, CancellationToken token = default) =>
        InvokeAsync<object, ArtifactDocument>(PlatformCapabilities.ArtifactRead, new { artifactId, includeArchived = false }, token);
    public Task<ArtifactRevision> ReviseAsync(CreateArtifactRevision request, CancellationToken token = default) =>
        InvokeAsync<CreateArtifactRevision, ArtifactRevision>(PlatformCapabilities.ArtifactRevise, request, token);
    public Task<ArtifactDocument> SubmitAsync(SubmitArtifactRevision request, CancellationToken token = default) =>
        InvokeAsync<SubmitArtifactRevision, ArtifactDocument>(PlatformCapabilities.ArtifactSubmit, request, token);
    public Task<ArtifactDocument> DecideAsync(DecideArtifactRevision request, CancellationToken token = default) =>
        InvokeAsync<DecideArtifactRevision, ArtifactDocument>(PlatformCapabilities.ArtifactDecide, request, token);
    public Task<ArtifactAccessRequest> RequestAccessAsync(RequestArtifactAccess request, CancellationToken token = default) =>
        InvokeAsync<RequestArtifactAccess, ArtifactAccessRequest>(PlatformCapabilities.ArtifactRequestAccess, request, token);
    public Task<ArtifactPackage> CreatePackageAsync(CreateArtifactPackage request, CancellationToken token = default) =>
        InvokeAsync<CreateArtifactPackage, ArtifactPackage>(PlatformCapabilities.ArtifactPackageCreate, request, token);
    public Task<ArtifactPackage> GetPackageAsync(Guid packageId, CancellationToken token = default) =>
        InvokeAsync<object, ArtifactPackage>(PlatformCapabilities.ArtifactPackageRead, new { packageId }, token);
    public Task<ArtifactPackage> SubmitPackageAsync(Guid packageId, string idempotencyKey, CancellationToken token = default) =>
        InvokeAsync<object, ArtifactPackage>(PlatformCapabilities.ArtifactPackageSubmit, new { packageId, idempotencyKey }, token);
    public Task<ArtifactPackage> DecidePackageAsync(Guid packageId, string idempotencyKey, CancellationToken token = default) =>
        InvokeAsync<object, ArtifactPackage>(PlatformCapabilities.ArtifactPackageDecide, new { packageId, idempotencyKey }, token);

    private async Task<TResponse> InvokeAsync<TRequest, TResponse>(string capability, TRequest payload, CancellationToken token)
    {
        var result = await _tools.InvokeAsync(capability, JsonSerializer.SerializeToElement(payload, JsonOptions), token);
        return result.Deserialize<TResponse>(JsonOptions) ?? throw new PlatformCapabilityException(capability,
            PlatformCapabilityErrorCode.ValidationFailed, "The artifact capability returned an empty response.");
    }
}

public sealed record CreateArtifactDocument(
    string Title, string Content, string DocumentType, string IdempotencyKey,
    Guid? FolderId = null, Guid? PackageId = null, Guid? OriginConversationId = null,
    Guid? OriginWorkItemId = null, Guid? StewardOrganizationUserId = null);
public sealed record ArtifactDocumentSummary(
    Guid Id, string Title, string DocumentType, string Status,
    Guid? LatestRevisionId, Guid? AcceptedRevisionId, DateTimeOffset UpdatedAt);
public sealed record ArtifactDocument(
    Guid Id, string Title, string DocumentType, string Status,
    Guid? LatestRevisionId, Guid? SubmittedRevisionId, Guid? AcceptedRevisionId,
    Guid? OriginConversationId, Guid? OriginWorkItemId, IReadOnlyList<ArtifactRevision> Revisions);
public sealed record ArtifactRevision(
    Guid Id, int Number, Guid? BaseRevisionId, string Content, string ContentSha256,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset? SubmittedAt, DateTimeOffset? DecidedAt);
public sealed record CreateArtifactRevision(
    Guid ArtifactId, Guid ExpectedBaseRevisionId, string Content, string IdempotencyKey);
public sealed record SubmitArtifactRevision(
    Guid ArtifactId, Guid RevisionId, string IdempotencyKey,
    Guid? ConversationId = null, Guid? ReviewerOrganizationUserId = null);
public sealed record DecideArtifactRevision(
    Guid ArtifactId, Guid RevisionId, string Decision, string? Comment, string IdempotencyKey,
    Guid? EvidenceConversationMessageId = null);
public sealed record RequestArtifactAccess(
    Guid ArtifactId, IReadOnlyList<string> Actions, string Justification, string IdempotencyKey,
    DateTimeOffset? ExpiresAt = null);
public sealed record ArtifactAccessRequest(
    Guid Id, Guid ArtifactId, string SubjectKind, Guid SubjectId, string SubjectDisplayName,
    IReadOnlyList<string> Actions, string Justification, string Status,
    DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? DecidedAt);
public sealed record ArtifactAccessDecision(
    Guid RequestId, Guid ArtifactId, string Outcome, IReadOnlyList<string> Actions,
    IReadOnlyList<Guid> GrantIds, IReadOnlyList<long> GrantRevisions, DateTimeOffset DecidedAt);
public sealed record ArtifactPackageMember(Guid ArtifactId, int Position, string RequiredDocumentType, Guid? AcceptedRevisionId = null);
public sealed record CreateArtifactPackage(string Name, string PackageType, IReadOnlyList<ArtifactPackageMember> Members, string IdempotencyKey);
public sealed record ArtifactPackage(Guid Id, string Name, string PackageType, int Version, string Status,
    IReadOnlyList<ArtifactPackageMember> Members, DateTimeOffset? AcceptedAt);
