using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.WorkManagement;

/// <summary>Typed, grant-governed access to C-Sweet work management.</summary>
public sealed class PlatformWorkClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPlatformToolInvoker _tools;

    internal PlatformWorkClient(IPlatformToolInvoker tools) => _tools = tools;

    public Task<IReadOnlyList<WorkBoardSummary>> ListBoardsAsync(
        WorkBoardListRequest? request = null,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkBoardListRequest, IReadOnlyList<WorkBoardSummary>>(
            WorkBoardCapabilities.Read, request ?? new(), cancellationToken);

    public Task<WorkBoardDetail> ReadBoardAsync(
        Guid boardId, CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkBoardReference, WorkBoardDetail>(
            WorkItemCapabilities.Read, new(boardId), cancellationToken);

    public Task<WorkItem> ReadItemAsync(
        WorkItemReference request, CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkItemReference, WorkItem>(
            WorkItemCapabilities.Read, request, cancellationToken);

    public Task<WorkBoardSummary> CreateBoardAsync(
        CreateWorkBoardRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<CreateWorkBoardRequest, WorkBoardSummary>(
            WorkBoardCapabilities.Create, request, cancellationToken);

    public Task<WorkBoardDetail> ConfigureBoardColumnsAsync(
        ConfigureWorkBoardColumnsRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<ConfigureWorkBoardColumnsRequest, WorkBoardDetail>(
            WorkBoardCapabilities.ConfigureColumns, request, cancellationToken);

    public Task<WorkItem> CreateItemAsync(
        CreateWorkItemRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<CreateWorkItemRequest, WorkItem>(
            WorkItemCapabilities.Create, request, cancellationToken);

    public Task<WorkItemComment> CommentAsync(
        CommentOnWorkItemRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<CommentOnWorkItemRequest, WorkItemComment>(
            WorkItemCapabilities.Comment, request, cancellationToken);

    public Task<WorkItem> EstimateAsync(
        EstimateWorkItemRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<EstimateWorkItemRequest, WorkItem>(
            WorkItemCapabilities.Estimate, request, cancellationToken);

    public Task<WorkItem> MoveItemAsync(
        MoveWorkItemRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<MoveWorkItemRequest, WorkItem>(
            WorkItemCapabilities.Move, request, cancellationToken);

    public Task<WorkOrchestrationPolicyRevision> ConfigureSoftwareTemplateAsync(
        ConfigureSoftwareOrchestrationTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        InvokeAsync<ConfigureSoftwareOrchestrationTemplateRequest, WorkOrchestrationPolicyRevision>(
            WorkOrchestrationCapabilities.ConfigureSoftwareTemplate, request, cancellationToken);

    public Task<IReadOnlyList<TeamRepositoryOption>> ListTeamRepositoryOptionsAsync(
        TeamRepositoryOptionsRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<TeamRepositoryOptionsRequest, IReadOnlyList<TeamRepositoryOption>>(
            GitRepositoryCapabilities.TeamOptions, request, cancellationToken);

    public Task<WorkItemTransfer> TransferAsync(
        TransferWorkItemRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<TransferWorkItemRequest, WorkItemTransfer>(
            WorkItemCapabilities.Transfer, request, cancellationToken);

    public Task<IReadOnlyList<WorkSprint>> ListSprintsAsync(
        Guid boardId, CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkBoardReference, IReadOnlyList<WorkSprint>>(
            WorkSprintCapabilities.Read, new(boardId), cancellationToken);

    public Task<WorkSprint> CreateSprintAsync(
        CreateWorkSprintRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<CreateWorkSprintRequest, WorkSprint>(
            WorkSprintCapabilities.Create, request, cancellationToken);

    public Task<WorkItem> SetItemSprintAsync(
        SetWorkItemSprintRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<SetWorkItemSprintRequest, WorkItem>(
            WorkSprintCapabilities.ManageScope, request, cancellationToken);

    public Task<WorkSprint> SetSprintCapacityAsync(
        SetWorkSprintCapacityRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<SetWorkSprintCapacityRequest, WorkSprint>(
            WorkSprintCapabilities.ManageCapacity, request, cancellationToken);

    public Task<WorkSprintCarryOver> CarryOverSprintAsync(
        CarryOverWorkSprintRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<CarryOverWorkSprintRequest, WorkSprintCarryOver>(
            WorkSprintCapabilities.CarryOver, request, cancellationToken);

    public Task<WorkSprintReport> ReadSprintReportAsync(
        Guid boardId, CancellationToken cancellationToken = default) =>
        InvokeAsync<WorkBoardReference, WorkSprintReport>(
            WorkSprintCapabilities.ReadReports, new(boardId), cancellationToken);

    private async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string capability, TRequest request, CancellationToken cancellationToken)
    {
        var result = await _tools.InvokeAsync(
            capability, JsonSerializer.SerializeToElement(request, JsonOptions), cancellationToken);
        try
        {
            return result.Deserialize<TResponse>(JsonOptions)
                ?? throw new PlatformCapabilityException(
                    capability, PlatformCapabilityErrorCode.ValidationFailed,
                    "The work-management capability returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new PlatformCapabilityException(
                capability, PlatformCapabilityErrorCode.ValidationFailed,
                "The work-management capability returned invalid JSON.", exception);
        }
    }
}
