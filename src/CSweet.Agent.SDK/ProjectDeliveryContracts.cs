namespace CSweet.Agent.SDK;

/// <summary>Joins an approved project to existing approved staffing; never hires or moves employees.</summary>
public static class ProjectDeliveryCapabilities
{
    public const string Prepare = CapabilityNames.ProjectDelivery.Prepare;
    public static readonly string[] All = [Prepare];
}
public sealed record PrepareProjectDeliveryRequest(Guid ProjectId, Guid StaffingRequestId,
    IReadOnlyList<Guid> ParticipantIds, long ExpectedProjectRevision, string IdempotencyKey);
public sealed record PreparedProjectDelivery(Guid ProjectId, Guid TeamId, Guid BoardId, long ProjectRevision)
{
    public CSweet.WorkManagement.Contracts.WorkstreamProfileReference? AvailableProfile { get; init; }
}
