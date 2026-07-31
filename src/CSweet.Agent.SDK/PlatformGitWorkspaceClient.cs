using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>
/// Typed, grant-governed Git workspace operations. Credentials are resolved by the
/// platform and are never returned to an agent.
/// </summary>
public sealed class PlatformGitWorkspaceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPlatformToolInvoker _tools;

    internal PlatformGitWorkspaceClient(IPlatformToolInvoker tools) => _tools = tools;

    public Task<GitWorkspaceResult> PrepareAsync(
        PrepareGitWorkspaceRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<PrepareGitWorkspaceRequest, GitWorkspaceResult>(
            GitWorkspaceCapabilities.Prepare, request, cancellationToken);

    public Task<GitWorkspaceInspection> InspectAsync(
        InspectGitWorkspaceRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<InspectGitWorkspaceRequest, GitWorkspaceInspection>(
            GitWorkspaceCapabilities.Inspect, request, cancellationToken);

    public Task<GitWorkspacePublication> PublishAsync(
        PublishGitWorkspaceRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<PublishGitWorkspaceRequest, GitWorkspacePublication>(
            GitWorkspaceCapabilities.Publish, request, cancellationToken);

    public Task<GitWorkspaceCleanupResult> CleanupAsync(
        CleanupGitWorkspaceRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<CleanupGitWorkspaceRequest, GitWorkspaceCleanupResult>(
            GitWorkspaceCapabilities.Cleanup, request, cancellationToken);

    private async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string capability,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _tools.InvokeAsync(
            capability,
            JsonSerializer.SerializeToElement(request, JsonOptions),
            cancellationToken);
        try
        {
            return result.Deserialize<TResponse>(JsonOptions)
                ?? throw new PlatformCapabilityException(
                    capability,
                    PlatformCapabilityErrorCode.ValidationFailed,
                    "The Git workspace capability returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new PlatformCapabilityException(
                capability,
                PlatformCapabilityErrorCode.ValidationFailed,
                "The Git workspace capability returned invalid JSON.",
                exception);
        }
    }
}

public sealed record PrepareGitWorkspaceRequest(
    Guid WorkItemId,
    long AssignmentRevision,
    Guid RepositoryConnectionId,
    string? BaseBranch,
    string BranchName,
    string IdempotencyKey)
{
    public string? ExpectedCommitSha { get; init; }
    public bool ResumePublishedBranch { get; init; }
}

public sealed record GitWorkspaceResult(
    Guid WorkspaceId,
    Guid WorkItemId,
    string Path,
    Guid RepositoryConnectionId,
    string BaseBranch,
    string BranchName,
    string Status,
    bool Resumed)
{
    public string? CheckoutCommitSha { get; init; }
}

public sealed record InspectGitWorkspaceRequest(Guid WorkspaceId);

public sealed record GitWorkspaceInspection(
    Guid WorkspaceId,
    string Status,
    bool HasChanges,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> Commits,
    IReadOnlyList<GitValidationResult> Validations)
{
    public bool HasTrackedChanges { get; init; }
    public IReadOnlyList<string> TrackedChangedFiles { get; init; } = [];
}

public sealed record GitValidationResult(
    string Command,
    bool Succeeded,
    int ExitCode,
    string? DiagnosticExcerpt = null);

public sealed record PublishGitWorkspaceRequest(
    Guid WorkspaceId,
    string CommitMessage,
    string PullRequestTitle,
    string PullRequestBody,
    string IdempotencyKey,
    IReadOnlyList<GitValidationResult>? Validations = null);

public sealed record GitWorkspacePublication(
    Guid WorkspaceId,
    string BranchName,
    string CommitSha,
    bool Pushed,
    Uri? PullRequestUrl,
    string Status)
{
    public string MergeStatus { get; init; } =
        CSweet.WorkManagement.Contracts.DeliveryMergeStatuses.None;
    public string? MergeCommitSha { get; init; }
    public DateTimeOffset? MergedAt { get; init; }
}

public sealed record CleanupGitWorkspaceRequest(
    Guid WorkspaceId,
    bool RetainOnFailure = true);

public sealed record GitWorkspaceCleanupResult(
    Guid WorkspaceId,
    bool Removed,
    DateTimeOffset? RetainUntil);
