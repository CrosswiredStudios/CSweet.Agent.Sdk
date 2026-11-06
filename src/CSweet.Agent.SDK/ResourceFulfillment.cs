using System.Text.Json;

namespace CSweet.Agent.SDK;

public static class ResourceCapabilities
{
    public const string Request = CapabilityNames.Resources.Request;
    public const string Read = CapabilityNames.Resources.Read;
    public const string Decide = CapabilityNames.Resources.Decide;
    public const string Changed = "com.csweet.resource.changed.v1";
    public const string BuildToolchain = "build-toolchain.v1";
}

public sealed record ResourceRequest(Guid WorkstreamId, string ResourceType, string ProfileKey,
    string Purpose, string IdempotencyKey);
public sealed record ResourceRead(Guid? RequestId = null, Guid? WorkstreamId = null, bool PendingApprovalOnly = false);
public sealed record ResourceDecision(Guid RequestId, long ExpectedRevision, string PlanHash,
    string Decision, string Comment, string IdempotencyKey);
public sealed record ResourcePlan(string HandlerKey, string Summary, Guid PackageVersionId, string PackageId,
    string PackageVersion, string ManifestDigest, string PackageDigest, Guid DefinitionId, string DefinitionDigest,
    string EnvironmentProfileKey, string EnvironmentImageDigest, IReadOnlyList<string> GrantedCapabilities,
    IReadOnlyList<string> GrantedSubscriptions, IReadOnlyList<string> GrantedRequestedCapabilities,
    decimal Cost, string Currency);
public sealed record ResourceRequestState(Guid Id, Guid WorkstreamId, Guid RequesterId, Guid ApproverId,
    Guid ApprovalId, string ResourceType, string ProfileKey, string Purpose, string Status, long Revision,
    string PlanHash, ResourcePlan Plan, string? Condition, Guid? ProviderInstallationId, Guid? CertificationRunId,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public int FulfillmentAttempt { get; init; } = 1;
    public string? ResumeStatus { get; init; }
}

/// <summary>Project capabilities fulfilled by trusted platform handlers after exact manager approval.</summary>
public sealed class PlatformResourceClient(PlatformCapabilityClient platform)
{
    public Task<ResourceRequestState> RequestAsync(ResourceRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<ResourceRequest, ResourceRequestState>(ResourceCapabilities.Request, request, token);
    public Task<IReadOnlyList<ResourceRequestState>> ReadAsync(ResourceRead request, CancellationToken token = default) =>
        platform.InvokeAsync<ResourceRead, IReadOnlyList<ResourceRequestState>>(ResourceCapabilities.Read, request, token);
    public Task<ResourceRequestState> DecideAsync(ResourceDecision decision, CancellationToken token = default) =>
        platform.InvokeAsync<ResourceDecision, ResourceRequestState>(ResourceCapabilities.Decide, decision, token);
}
