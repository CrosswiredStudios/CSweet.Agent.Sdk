using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>Durable instruction routing and reporting; no call expands execution grants.</summary>
public sealed class PlatformDelegationClient(PlatformCapabilityClient platform)
{
    public Task<DelegationSnapshot> DelegateWithTrackingAsync(DelegateWithTrackingRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<DelegateWithTrackingRequest, DelegationSnapshot>(DelegationCapabilities.Create, request, token);
    public Task<IReadOnlyList<DelegationSnapshot>> ReadAsync(ReadDelegationRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<ReadDelegationRequest, IReadOnlyList<DelegationSnapshot>>(DelegationCapabilities.Read, request, token);
    public Task<DelegationSnapshot> RespondAsync(DelegationResponseRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<DelegationResponseRequest, DelegationSnapshot>(DelegationCapabilities.Respond, request, token);
    public Task<DelegationSnapshot> PublishReportAsync(PublishDelegationReportRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<PublishDelegationReportRequest, DelegationSnapshot>(DelegationCapabilities.Publish, request, token);
    public Task<DelegationSnapshot> CancelAsync(CancelDelegationRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<CancelDelegationRequest, DelegationSnapshot>(DelegationCapabilities.Cancel, request, token);
}

/// <summary>Authorized subscriptions to canonical project and personal work.</summary>
public sealed class PlatformWorkTrackingClient(PlatformCapabilityClient platform)
{
    public Task<WorkTrackingSnapshot> MarkProcessedAsync(ProcessTrackingUpdateRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<ProcessTrackingUpdateRequest, WorkTrackingSnapshot>(DelegationCapabilities.Processed, request, token);
    public Task<WorkTrackingSnapshot> FollowAsync(FollowWorkRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<FollowWorkRequest, WorkTrackingSnapshot>(DelegationCapabilities.Follow, request, token);
    public Task<WorkTrackingSnapshot> UnfollowAsync(FollowWorkRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<FollowWorkRequest, WorkTrackingSnapshot>(DelegationCapabilities.Unfollow, request, token);
    public Task<WorkTrackingSnapshot> ReadAsync(ReadWorkTrackingRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<ReadWorkTrackingRequest, WorkTrackingSnapshot>(DelegationCapabilities.ReadTracking, request, token);
    public Task<TrackingUpdate> ReportProgressAsync(WorkProgressRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<WorkProgressRequest, TrackingUpdate>(DelegationCapabilities.Progress, request, token);
    public Task<IReadOnlyList<PendingTrackingUpdate>> PendingUpdatesAsync(PendingTrackingRequest request, CancellationToken token = default) =>
        platform.InvokeAsync<PendingTrackingRequest, IReadOnlyList<PendingTrackingUpdate>>(DelegationCapabilities.Pending, request, token);
}
