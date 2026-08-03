using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>
/// Business-governed source-control inventory, provisioning, and exact-SHA merge decisions.
/// Provider credentials and administrative provider operations are never exposed through this API.
/// </summary>
public sealed class PlatformSourceControlClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPlatformToolInvoker _tools;

    internal PlatformSourceControlClient(IPlatformToolInvoker tools) => _tools = tools;

    public Task<IReadOnlyList<CSweet.WorkManagement.Contracts.TeamRepositoryOption>>
        ListTeamRepositoryOptionsAsync(
            CSweet.WorkManagement.Contracts.TeamRepositoryOptionsRequest request,
            CancellationToken cancellationToken = default) =>
        InvokeAsync<CSweet.WorkManagement.Contracts.TeamRepositoryOptionsRequest,
            IReadOnlyList<CSweet.WorkManagement.Contracts.TeamRepositoryOption>>(
            SourceControlCapabilities.TeamRepositoryOptions,
            request,
            cancellationToken);

    public Task<RepositoryProvisioningResult> ProvisionRepositoryAsync(
        ProvisionSourceControlRepositoryRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<ProvisionSourceControlRepositoryRequest, RepositoryProvisioningResult>(
            SourceControlCapabilities.ProvisionRepository,
            request,
            cancellationToken);

    public Task<GitMergeReview> ReviewMergeAsync(
        ReviewGitMergeRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<ReviewGitMergeRequest, GitMergeReview>(
            GitMergeCapabilities.Review,
            request,
            cancellationToken);

    public Task<GitMergeAuthorizationResult> AuthorizeMergeAsync(
        AuthorizeGitMergeRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<AuthorizeGitMergeRequest, GitMergeAuthorizationResult>(
            GitMergeCapabilities.Authorize,
            request,
            cancellationToken);

    private async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string capability,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _tools.InvokeAsync(
            capability,
            JsonSerializer.SerializeToElement(request, JsonOptions),
            cancellationToken);
        return result.Deserialize<TResponse>(JsonOptions)
            ?? throw new PlatformCapabilityException(
                capability,
                PlatformCapabilityErrorCode.ValidationFailed,
                "The source-control capability returned an empty response.");
    }
}

public sealed record ProvisionSourceControlRepositoryRequest(
    Guid ProductOrWorkstreamId,
    string ProjectDisplayName,
    string? Description,
    Guid TemplateId,
    string IdempotencyKey);

public sealed record RepositoryProvisioningResult(
    Guid RequestId,
    string Status,
    Guid? RepositoryId,
    Guid? ApprovalId,
    string? Remediation);

public sealed record ReviewGitMergeRequest(
    Guid WorkItemId,
    long AssignmentRevision,
    string IdempotencyKey);

public sealed record GitMergeReview(
    Guid PublicationId,
    Guid RepositoryId,
    Guid WorkItemId,
    string RepositoryName,
    string CandidateCommitSha,
    Uri? PullRequestUrl,
    string DiffSummary,
    IReadOnlyList<GitValidationResult> QualityEvidence,
    IReadOnlyList<string> RequiredChecks,
    string Status);

public static class GitMergeDecisions
{
    public const string Approve = "Approve";
    public const string Reject = "Reject";
}

public sealed record AuthorizeGitMergeRequest(
    Guid WorkItemId,
    long AssignmentRevision,
    Guid PublicationId,
    string CandidateCommitSha,
    string Decision,
    string? Feedback,
    string IdempotencyKey);

public sealed record GitMergeAuthorizationResult(
    Guid PublicationId,
    string CandidateCommitSha,
    string Decision,
    string Status,
    DateTimeOffset DecidedAt,
    Guid? AdministratorApprovalId);
