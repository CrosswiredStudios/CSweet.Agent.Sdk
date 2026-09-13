using System.Text;
using System.Text.Json.Serialization;

namespace CSweet.Agent.SDK.Compute;

public static class ComputeCapabilities
{
    public const string Provision = "compute.provision.v1";
    public const string Read = "compute.read.v1";
    public const string List = "compute.list.v1";
    public const string Start = "compute.start.v1";
    public const string Stop = "compute.stop.v1";
    public const string Restart = "compute.restart.v1";
    public const string Destroy = "compute.destroy.v1";
    public const string Execute = "compute.execute.v1";
    public const string Inbound = "network.inbound.v1";
    public const string PublishPort = "network.publish-port.v1";
}

public static class ComputeEvents
{
    public const string Changed = "com.csweet.compute.changed.v1";
}

/// <summary>A wake hint. Re-read current authorized state; this event never grants execution authority.</summary>
public sealed record ComputeChangedEvent(Guid EnvironmentId, long Revision);

public sealed record ComputeResources(int CpuCount, long MemoryMiB, long DiskMiB, int GpuCount = 0);

/// <summary>Requested reachability. It does not select a host interface, address, or firewall rule.</summary>
public sealed record ComputeNetworkPolicy(string Mode = "none", bool AllowOutbound = false,
    bool PublicEndpoint = false, IReadOnlyList<int>? PublishedPorts = null);

/// <summary>Provider-independent requirements. OS and architecture identifiers include linux/windows and x64/arm64.
/// Template IDs come from platform configuration; agents never provide image paths or credentials.</summary>
public sealed record ComputeSpecification(string OperatingSystem, string Architecture, string TemplateId,
    ComputeResources Resources, int LifetimeSeconds, string Persistence = "ephemeral",
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ComputeNetworkPolicy? Network = null);

public sealed record ProvisionComputeRequest(Guid WorkstreamId, string DesiredEnvironmentKey,
    string IdempotencyKey, ComputeSpecification Specification);

public sealed record ComputeLifecycleRequest(Guid EnvironmentId, long ExpectedGeneration, string IdempotencyKey);

/// <summary>Guest-local executable and arguments. No host process or filesystem access is implied.</summary>
public sealed record ComputeCommand(Guid RequestId, string Executable, string WorkingDirectory,
    IReadOnlyList<string> Arguments, int TimeoutSeconds = 30, int MaximumOutputBytes = 8192);

public sealed record ExecuteComputeCommandRequest(Guid EnvironmentId, long ExpectedGeneration,
    string IdempotencyKey, ComputeCommand Command);

public sealed record PublishComputePortRequest(Guid EnvironmentId, long ExpectedGeneration,
    string IdempotencyKey, int GuestPort);

public sealed record ComputeEnvironment(Guid Id, long Revision, long Generation, string DesiredState,
    string State, string Persistence, DateTimeOffset CreatedAt, DateTimeOffset LeaseExpiresAt,
    string? FailureCode, string? DesiredEnvironmentKey = null);

public sealed record ComputeEnvironmentPage(IReadOnlyList<ComputeEnvironment> Items, Guid? NextAfterId);

/// <summary>Status values include Pending, Dispatching, Completed, Blocked and Superseded.
/// Completed means the operation finished; inspect Result to determine success.</summary>
public sealed record ComputeOperation(Guid Id, Guid EnvironmentId, long Generation, string Status,
    string? FailureCode, ComputeWorkloadResult? Result);

public sealed record ComputeWorkloadResult(ComputeCommandResult? Command = null, string? Url = null,
    DateTimeOffset? UrlExpiresAt = null, string? ErrorCode = null);

/// <summary>Output is byte-exact; text accessors decode UTF-8 for convenience. An absent exit code or
/// an error such as outcome-unknown is not success and must not cause automatic command re-execution.</summary>
public sealed record ComputeCommandResult(Guid RequestId, int? ExitCode, bool TimedOut,
    byte[]? StandardOutput = null, byte[]? StandardError = null, bool Truncated = false, string? ErrorCode = null)
{
    [JsonIgnore] public string StandardOutputText => Encoding.UTF8.GetString(StandardOutput ?? []);
    [JsonIgnore] public string StandardErrorText => Encoding.UTF8.GetString(StandardError ?? []);
}
