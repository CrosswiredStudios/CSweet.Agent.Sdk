using System.Text.Json;
using CSweet.WorkManagement.Contracts;
namespace CSweet.Agent.SDK;

public static class ProjectIntakeCapabilities
{
    public const string Retain = CapabilityNames.ProjectIntake.Retain;
    public const string Read = CapabilityNames.ProjectIntake.Read;
    public const string List = CapabilityNames.ProjectIntake.List;
    public const string Discover = CapabilityNames.ProjectIntake.Discover;
    public const string Choose = CapabilityNames.ProjectIntake.Choose;
    public const string Start = CapabilityNames.ProjectIntake.Start;
    public const string Manager = CapabilityNames.ProjectIntake.Manager;
    public const string Changed = "com.csweet.project-intake.changed.v1";
    public const string ManagerRequested = "com.csweet.project-intake.manager-requested.v1";
    public const string Staffing = CapabilityNames.ProjectIntake.Staffing;
    public const string AssistanceList = CapabilityNames.ProjectIntake.AssistanceList;
    public const string ManagerSetup = CapabilityNames.ProjectIntake.ManagerSetup;
    public static readonly string[] All = [Retain, Read, List, Discover, Choose, Start, Manager, Staffing, ManagerSetup, AssistanceList];
}
public sealed record RetainProjectIntakeRequest(Guid ConversationId, Guid SourceMessageId, string Name, string Goal,
    string TicketOwner, Guid? EnvironmentId, string IdempotencyKey);
public sealed record ProjectIntakeReference(Guid IntakeId);
public sealed record ChooseProjectIntakeRequest(Guid IntakeId, long ExpectedRevision, string Choice, Guid? ProjectId,
    Guid SourceMessageId, string IdempotencyKey);
public sealed record StartProjectIntakeRequest(Guid IntakeId, long ExpectedRevision, string IdempotencyKey);
public sealed record ProjectIntakeSummary(Guid Id, string Name, string Goal, string Status, string TicketOwner,
    Guid RequestingHumanId, Guid DeveloperId, Guid? ProjectId, Guid? BoardId, Guid? RootItemId,
    Guid? ManagerId, Guid? TeamId, Guid ConversationId, Guid SourceMessageId, long Revision, string SetupUrl, string? Issue)
{
    public string OriginalRequest { get; init; } = string.Empty;
    public Guid? SetupChoiceMessageId { get; init; }
}
public sealed record ProjectCandidate(Guid Id, string Name, string Goal, Guid ManagerId, Guid? TeamId,
    bool DeveloperAssigned, long Revision);
public sealed record ProjectIntakeChanged(Guid IntakeId, long Revision);
public sealed record ProjectManagerAssistanceRequest(Guid IntakeId, string Name, string Goal, Guid DeveloperId,
    Guid RequestingHumanId, Guid? TeamId)
{
    public string OriginalRequest { get; init; } = string.Empty;
    public string UserChoice { get; init; } = "manager";
}
public sealed record ProjectStaffingRequest(Guid IntakeId, Guid? ManagerId, string IdempotencyKey);
public sealed record ProjectStaffingResult(ProjectIntakeSummary Intake, IReadOnlyList<ProjectManagerCandidate> Candidates,
    Guid? HiringRecommendationId);
public sealed record ProjectManagerCandidate(Guid Id, string Name, Guid? TeamId);

/// <summary>Project prerequisites and attachment of previously approved staffing.</summary>
public sealed class PlatformProjectClient
{
    private readonly IPlatformToolInvoker tools;
    internal PlatformProjectClient(IPlatformToolInvoker tools) => this.tools = tools;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Task<ProjectIntakeSummary> RetainAsync(RetainProjectIntakeRequest request, CancellationToken ct = default) => Call<RetainProjectIntakeRequest, ProjectIntakeSummary>(ProjectIntakeCapabilities.Retain, request, ct);
    public Task<ProjectIntakeSummary> ReadAsync(Guid id, CancellationToken ct = default) => Call<ProjectIntakeReference, ProjectIntakeSummary>(ProjectIntakeCapabilities.Read, new(id), ct);
    public Task<IReadOnlyList<ProjectIntakeSummary>> ListAsync(CancellationToken ct = default) => Call<object, IReadOnlyList<ProjectIntakeSummary>>(ProjectIntakeCapabilities.List, new { }, ct);
    public Task<IReadOnlyList<ProjectCandidate>> DiscoverAsync(Guid id, CancellationToken ct = default) => Call<ProjectIntakeReference, IReadOnlyList<ProjectCandidate>>(ProjectIntakeCapabilities.Discover, new(id), ct);
    public Task<ProjectIntakeSummary> ChooseAsync(ChooseProjectIntakeRequest request, CancellationToken ct = default) => Call<ChooseProjectIntakeRequest, ProjectIntakeSummary>(ProjectIntakeCapabilities.Choose, request, ct);
    public Task<PersonalTodoItem> StartAsync(StartProjectIntakeRequest request, CancellationToken ct = default) => Call<StartProjectIntakeRequest, PersonalTodoItem>(ProjectIntakeCapabilities.Start, request, ct);
    public Task<ProjectIntakeSummary> RequestManagerAsync(ChooseProjectIntakeRequest request, CancellationToken ct = default) => Call<ChooseProjectIntakeRequest, ProjectIntakeSummary>(ProjectIntakeCapabilities.Manager, request, ct);
    public Task<IReadOnlyList<ProjectIntakeSummary>> ListAssistanceAsync(CancellationToken ct = default) => Call<object, IReadOnlyList<ProjectIntakeSummary>>(ProjectIntakeCapabilities.AssistanceList, new { }, ct);
    public Task<ProjectStaffingResult> StaffAsync(ProjectStaffingRequest request, CancellationToken ct = default) => Call<ProjectStaffingRequest, ProjectStaffingResult>(ProjectIntakeCapabilities.Staffing, request, ct);
    public Task<ProjectIntakeSummary> ReadManagerSetupAsync(Guid id, CancellationToken ct = default) => Call<ProjectIntakeReference, ProjectIntakeSummary>(ProjectIntakeCapabilities.ManagerSetup, new(id), ct);
    public Task<PreparedProjectDelivery> PrepareDeliveryAsync(PrepareProjectDeliveryRequest request, CancellationToken ct = default) => Call<PrepareProjectDeliveryRequest, PreparedProjectDelivery>(ProjectDeliveryCapabilities.Prepare, request, ct);
    private async Task<TResponse> Call<TRequest,TResponse>(string capability,TRequest request,CancellationToken ct) =>
        (await tools.InvokeAsync(capability,JsonSerializer.SerializeToElement(request,Json),ct)).Deserialize<TResponse>(Json)
        ?? throw new PlatformCapabilityException(capability,PlatformCapabilityErrorCode.ValidationFailed,"Project intake returned no result.");
}
