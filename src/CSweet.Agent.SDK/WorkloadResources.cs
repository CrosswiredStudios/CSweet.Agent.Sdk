namespace CSweet.Agent.SDK;

public static class WorkloadResourceEvents
{
    public const string Changed = "com.csweet.runtime.resources.changed.v1";
}

public sealed record WorkloadResourcesChanged(Guid WorkId, int Generation);

/// <summary>Trusted allocation and policy bounds for the current callback. MiB includes guest overhead.</summary>
public sealed record WorkloadResources(int MemoryMb, int MaximumMemoryMb, int ReplacementCount,
    string RecoveryState = "Running", string? ProfileKey = null, Guid? ResumeWorkItemId = null, string? ToolchainFingerprint = null, int MaximumReplacements = 2);

public sealed record WorkloadResourceHint(string CommandClass, string InputFingerprint, int MemoryMb);

public sealed record WorkspaceValidationReceipt(string Command, bool Succeeded, int ExitCode);
public sealed record WorkspaceResumeSummary(string SourceFingerprint, IReadOnlyList<WorkspaceValidationReceipt> Validations);

/// <summary>A completed, server-retained source checkpoint. It is evidence, never an execution grant.</summary>
public sealed record WorkspaceCheckpointReceipt(Guid WorkspaceId, long AssignmentRevision, string Digest);

/// <summary>Advisory request. The platform rechecks the lease, checkpoint, policy and capacity.</summary>
public sealed record WorkloadResourceRequest(int MemoryMb, WorkspaceCheckpointReceipt Checkpoint,
    string CommandClass, string InputFingerprint, string Reason);

/// <summary>Ends a callback after all writers have stopped and a checkpoint has completed.</summary>
public sealed class WorkloadResourceHandoffException(WorkloadResourceRequest request) : Exception(
    "runtime.resources.handoff: source checkpoint saved; a larger isolated runtime is required.")
{
    public WorkloadResourceRequest Request { get; } = request;
}
