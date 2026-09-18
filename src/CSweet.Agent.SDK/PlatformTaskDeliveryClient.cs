namespace CSweet.Agent.SDK;

public static class TaskDeliveryCapabilities
{
    public const string Submit = CapabilityNames.TaskDelivery.Submit;
    public const string List = CapabilityNames.TaskDelivery.List;
    public const string Read = CapabilityNames.TaskDelivery.Read;
    public const string Decide = CapabilityNames.TaskDelivery.Decide;
    public const string Quality = CapabilityNames.TaskDelivery.Quality;
    public const string Preferences = CapabilityNames.TaskDelivery.Preferences;
    public const string ChangePreference = CapabilityNames.TaskDelivery.ChangePreference;
    public const string ReviewRequested = "com.csweet.source-control.task-review.requested.v1";
    public const string Changed = "com.csweet.source-control.task-review.changed.v1";
}
public sealed record SubmitTaskReviewRequest(Guid RootItemId, Guid TaskItemId, Guid PublicationId, string Summary, string IdempotencyKey);
public sealed record DecideTaskReviewRequest(Guid ReviewId, long ExpectedRevision, Guid SourceMessageId, string Choice, string IdempotencyKey);
public sealed record ReadTaskReviewRequest(Guid TaskItemId);
public sealed record TaskReviewChanged(Guid TaskItemId, Guid RootItemId, long Revision);
public sealed record TaskReviewResult(Guid Id, Guid TaskItemId, Guid RootItemId, Guid RepositoryId,
    string Title, string Description, IReadOnlyList<string> AcceptanceCriteria, string CommitSha,
    string Status, string QualityStatus, string? Failure, Guid? QaInstallationId, long AssignmentRevision, long Revision);
public sealed record ReportTaskQualityRequest(Guid ReviewId, string CommitSha, string Verdict,
    string Summary, IReadOnlyList<GitValidationResult> Validations, string IdempotencyKey);
public sealed record ReadMergePreferencesRequest(Guid ScopeWorkItemId);
public sealed record MergePreferenceResult(Guid ScopeWorkItemId, string Title, string ScopeKind,
    string Mode, long Revision, Guid? InheritedFromId, string EffectiveMode);
public sealed record ChangeMergePreferenceRequest(Guid ScopeWorkItemId, string Mode, Guid SourceMessageId,
    long ExpectedRevision, string IdempotencyKey);

public sealed partial class PlatformSourceControlClient
{
    public Task<TaskReviewResult> DecideTaskReviewAsync(DecideTaskReviewRequest request, CancellationToken ct = default) =>
        InvokeAsync<DecideTaskReviewRequest, TaskReviewResult>(TaskDeliveryCapabilities.Decide, request, ct);
    public Task<TaskReviewResult> SubmitTaskReviewAsync(SubmitTaskReviewRequest request, CancellationToken ct = default) =>
        InvokeAsync<SubmitTaskReviewRequest, TaskReviewResult>(TaskDeliveryCapabilities.Submit, request, ct);
    public Task<IReadOnlyList<TaskReviewResult>> ListTaskReviewsAsync(CancellationToken ct = default) =>
        InvokeAsync<object, IReadOnlyList<TaskReviewResult>>(TaskDeliveryCapabilities.List, new { }, ct);
    public Task<TaskReviewResult> ReadTaskReviewAsync(ReadTaskReviewRequest request, CancellationToken ct = default) =>
        InvokeAsync<ReadTaskReviewRequest, TaskReviewResult>(TaskDeliveryCapabilities.Read, request, ct);
    public Task<TaskReviewResult> ReportTaskQualityAsync(ReportTaskQualityRequest request, CancellationToken ct = default) =>
        InvokeAsync<ReportTaskQualityRequest, TaskReviewResult>(TaskDeliveryCapabilities.Quality, request, ct);
    public Task<MergePreferenceResult> ReadMergePreferencesAsync(ReadMergePreferencesRequest request, CancellationToken ct = default) =>
        InvokeAsync<ReadMergePreferencesRequest, MergePreferenceResult>(TaskDeliveryCapabilities.Preferences, request, ct);
    public Task<MergePreferenceResult> ChangeMergePreferenceAsync(ChangeMergePreferenceRequest request, CancellationToken ct = default) =>
        InvokeAsync<ChangeMergePreferenceRequest, MergePreferenceResult>(TaskDeliveryCapabilities.ChangePreference, request, ct);
}
