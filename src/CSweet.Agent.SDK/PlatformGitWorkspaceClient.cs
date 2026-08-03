using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>
/// Typed operations over an assignment-scoped, credential-free source snapshot. Core derives the
/// repository, base commit, branch, and provider policy; callers cannot supply any of them.
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

    public Task<GitWorkspaceRefreshResult> RefreshAsync(
        RefreshGitWorkspaceRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<RefreshGitWorkspaceRequest, GitWorkspaceRefreshResult>(
            GitWorkspaceCapabilities.Refresh, request, cancellationToken);

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
    string IdempotencyKey);

public sealed record GitWorkspaceResult(
    Guid WorkspaceId,
    Guid WorkItemId,
    string Path,
    Guid RepositoryId,
    string Provider,
    string DeliveryKind,
    string BaseCommitSha,
    string Status,
    bool Resumed);

public sealed record RefreshGitWorkspaceRequest(
    Guid WorkspaceId,
    long AssignmentRevision,
    string IdempotencyKey);

public sealed record GitWorkspaceConflict(
    string Path,
    string Kind,
    string Message);

public sealed record GitWorkspaceRefreshResult(
    Guid WorkspaceId,
    string Status,
    string BaseCommitSha,
    IReadOnlyList<GitWorkspaceConflict> Conflicts);

public sealed record InspectGitWorkspaceRequest(
    Guid WorkspaceId,
    long AssignmentRevision);

public sealed record GitWorkspaceInspection(
    Guid WorkspaceId,
    string Status,
    bool HasChanges,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<GitValidationResult> Validations)
{
    public bool HasTrackedChanges { get; init; }
    public IReadOnlyList<string> TrackedChangedFiles { get; init; } = [];
    public string? DiffSummary { get; init; }
}

public sealed record GitValidationResult(
    string Command,
    bool Succeeded,
    int ExitCode,
    string? DiagnosticExcerpt = null);

public sealed record PublishGitWorkspaceRequest(
    Guid WorkspaceId,
    long AssignmentRevision,
    string CommitMessage,
    string ProposedChangeTitle,
    string ProposedChangeBody,
    string IdempotencyKey,
    IReadOnlyList<GitValidationResult>? Validations = null);

public static class GitDeliveryKinds
{
    public const string PullRequest = "PullRequest";
    public const string BranchOnly = "BranchOnly";
}

public sealed record GitWorkspacePublication(
    Guid PublicationId,
    Guid WorkspaceId,
    Guid RepositoryId,
    string Provider,
    string DeliveryKind,
    string BranchName,
    string CommitSha,
    Uri? PullRequestUrl,
    string Status);

public sealed record CleanupGitWorkspaceRequest(
    Guid WorkspaceId,
    long AssignmentRevision,
    bool RetainOnFailure = true);

public sealed record GitWorkspaceCleanupResult(
    Guid WorkspaceId,
    bool Removed,
    DateTimeOffset? RetainUntil);
