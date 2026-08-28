using System.Text.Json;
using CSweet.Agent.SDK.WorkManagement;
using Microsoft.Extensions.AI;

namespace CSweet.Agent.SDK;

/// <summary>Typed, grant-governed access to authoritative C-Sweet platform services.</summary>
public sealed class PlatformCapabilityClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPlatformToolInvoker _tools;

    internal PlatformCapabilityClient(IPlatformToolInvoker tools)
    {
        _tools = tools;
        Lifecycle = new PlatformAgentLifecycleClient(tools);
        Memory = new PlatformMemoryClient(tools);
        Work = new PlatformWorkClient(tools);
        PersonalTodo = new PlatformPersonalTodoClient(tools);
        Artifacts = new PlatformArtifactClient(tools);
        Git = new PlatformGitWorkspaceClient(tools);
        SourceControl = new PlatformSourceControlClient(tools);
        Communication = new PlatformCommunicationClient(this);
    }

    internal IPlatformToolInvoker Tools => _tools;

    public PlatformAgentLifecycleClient Lifecycle { get; }
    public PlatformMemoryClient Memory { get; }
    public PlatformWorkClient Work { get; }
    public PlatformPersonalTodoClient PersonalTodo { get; }
    public PlatformArtifactClient Artifacts { get; }
    public PlatformGitWorkspaceClient Git { get; }
    public PlatformSourceControlClient SourceControl { get; }
    public PlatformCommunicationClient Communication { get; }

    public Task<BusinessProfileResponse> ReadBusinessProfileAsync(CancellationToken token = default) =>
        InvokeAsync<object, BusinessProfileResponse>(PlatformCapabilities.BusinessProfileRead, new { }, token);

    public Task<MutationResponse> UpdateExplicitBusinessProfileAsync(ExplicitBusinessProfileUpdateRequest request, CancellationToken token = default) =>
        InvokeAsync<ExplicitBusinessProfileUpdateRequest, MutationResponse>(PlatformCapabilities.BusinessProfileUpdateExplicit, request, token);

    public Task<MutationResponse> ProposeBusinessProfileUpdateAsync(ProposedProfileUpdateRequest request, CancellationToken token = default) =>
        InvokeAsync<ProposedProfileUpdateRequest, MutationResponse>(PlatformCapabilities.BusinessProfileProposeUpdate, request, token);

    public Task<OrganizationSnapshotResponse> ReadOrganizationSnapshotAsync(CancellationToken token = default) =>
        InvokeAsync<object, OrganizationSnapshotResponse>(PlatformCapabilities.OrganizationSnapshotRead, new { }, token);

    public Task<BusinessPatternSearchResponse> SearchBusinessPatternsAsync(BusinessPatternSearchRequest request, CancellationToken token = default) =>
        InvokeAsync<BusinessPatternSearchRequest, BusinessPatternSearchResponse>(PlatformCapabilities.BusinessPatternSearch, request, token);

    public Task<WorkforceSearchResponse> SearchWorkforceAsync(WorkforceSearchRequest request, CancellationToken token = default) =>
        InvokeAsync<WorkforceSearchRequest, WorkforceSearchResponse>(PlatformCapabilities.WorkforceSearch, request, token);

    public Task<MutationResponse> ProposeWorkstreamAsync(WorkstreamPlanProposalRequest request, CancellationToken token = default) =>
        InvokeAsync<WorkstreamPlanProposalRequest, MutationResponse>(PlatformCapabilities.WorkstreamPlanPropose, request, token);

    public Task<MutationResponse> ProposeWorkforcePlanAsync(WorkforcePlanProposalRequest request, CancellationToken token = default) =>
        InvokeAsync<WorkforcePlanProposalRequest, MutationResponse>(PlatformCapabilities.WorkforcePlanPropose, request, token);

    public Task<FinancialOperatingProfileResponse> ReadFinanceProfileAsync(CancellationToken token = default) =>
        InvokeAsync<object, FinancialOperatingProfileResponse>(PlatformCapabilities.FinanceProfileRead, new { }, token);

    public Task<MutationResponse> ProposeFinanceProfileUpdateAsync(FinancialProfileProposalRequest request, CancellationToken token = default) =>
        InvokeAsync<FinancialProfileProposalRequest, MutationResponse>(PlatformCapabilities.FinanceProfileProposeUpdate, request, token);

    public Task<BudgetEvaluationResponse> EvaluateBudgetAsync(BudgetEvaluationRequest request, CancellationToken token = default) =>
        InvokeAsync<BudgetEvaluationRequest, BudgetEvaluationResponse>(PlatformCapabilities.BudgetEvaluate, request, token);

    public Task<ApprovalProposalResponse> ProposeApprovalAsync(ApprovalProposalRequest request, CancellationToken token = default) =>
        InvokeAsync<ApprovalProposalRequest, ApprovalProposalResponse>(PlatformCapabilities.ApprovalPropose, request, token);

    public Task<ManagementCycleResponse> ReadManagementCycleAsync(CancellationToken token = default) =>
        InvokeAsync<object, ManagementCycleResponse>(PlatformCapabilities.ManagementCycleRead, new { }, token);

    public Task<UserQuestionResponse> AskUserAsync(AskUserRequest request, CancellationToken token = default) =>
        InvokeAsync<AskUserRequest, UserQuestionResponse>(PlatformCapabilities.UserInputRequest, request, token);

    public Task<HiringBacklogResponse> ListHiringRecommendationsAsync(CancellationToken token = default) =>
        InvokeAsync<object, HiringBacklogResponse>(PlatformCapabilities.HiringRecommendationList, new { }, token);

    public Task<HiringRecommendationResponse> UpsertHiringRecommendationAsync(UpsertHiringRecommendationRequest request, CancellationToken token = default) =>
        InvokeAsync<UpsertHiringRecommendationRequest, HiringRecommendationResponse>(PlatformCapabilities.HiringRecommendationUpsert, request, token);

    public Task<HiringRecommendationResponse> ResolveHiringRecommendationAsync(ResolveHiringRecommendationRequest request, CancellationToken token = default) =>
        InvokeAsync<ResolveHiringRecommendationRequest, HiringRecommendationResponse>(PlatformCapabilities.HiringRecommendationResolve, request, token);

    public Task<HiringRecommendationResponse> WithdrawHiringRecommendationAsync(WithdrawHiringRecommendationRequest request, CancellationToken token = default) =>
        InvokeAsync<WithdrawHiringRecommendationRequest, HiringRecommendationResponse>(PlatformCapabilities.HiringRecommendationWithdraw, request, token);

    public Task<ResourceChangeRequestResponse> ProposeResourceChangeAsync(ResourceChangeProposalRequest request, CancellationToken token = default) =>
        InvokeAsync<ResourceChangeProposalRequest, ResourceChangeRequestResponse>(PlatformCapabilities.ResourceChangePropose, request, token);

    public Task<ResourceChangeReadResponse> ReadResourceChangesAsync(ResourceChangeReadRequest request, CancellationToken token = default) =>
        InvokeAsync<ResourceChangeReadRequest, ResourceChangeReadResponse>(PlatformCapabilities.ResourceChangeRead, request, token);

    public Task<ResourceChangeRequestResponse> DecideResourceChangeAsync(ResourceChangeDecisionRequest request, CancellationToken token = default) =>
        InvokeAsync<ResourceChangeDecisionRequest, ResourceChangeRequestResponse>(PlatformCapabilities.ResourceChangeDecide, request, token);

    public Task<SuggestedUserActionResponse> SuggestUserActionAsync(SuggestUserActionRequest request, CancellationToken token = default) =>
        InvokeAsync<SuggestUserActionRequest, SuggestedUserActionResponse>(PlatformCapabilities.UserActionSuggest, request, token);

    public Task<HiringWorkflowResponse> StageHiringWorkflowAsync(StageHiringWorkflowRequest request, CancellationToken token = default) =>
        InvokeAsync<StageHiringWorkflowRequest, HiringWorkflowResponse>(PlatformCapabilities.HiringWorkflowStage, request, token);

    public Task<TeamRosterResponse> ReadTeamRosterAsync(
        TeamRosterRequest? request = null,
        CancellationToken token = default) =>
        InvokeAsync<TeamRosterRequest, TeamRosterResponse>(
            PlatformCapabilities.TeamRosterRead,
            request ?? new TeamRosterRequest(),
            token);

    /// <summary>Reads a stable, complete roster and retries the full paging pass once on revision drift.</summary>
    public async Task<AgentTeamContext?> ReadCompleteTeamRosterAsync(
        int pageSize = 50,
        CancellationToken token = default)
    {
        if (pageSize is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(pageSize));

        for (var pass = 0; pass < 2; pass++)
        {
            AgentTeamContext? first = null;
            var members = new List<AgentTeammate>();
            var page = 1;
            while (true)
            {
                var team = (await ReadTeamRosterAsync(new TeamRosterRequest(page, pageSize), token)).Team;
                if (team is null)
                    return null;
                first ??= team;
                if (team.TeamId != first.TeamId || team.Revision != first.Revision)
                    break;

                members.AddRange(team.Members);
                if (!team.HasMore)
                    return first with { Members = members, TotalMemberCount = members.Count, HasMore = false };
                page++;
            }
        }

        throw new PlatformCapabilityException(
            PlatformCapabilities.TeamRosterRead,
            PlatformCapabilityErrorCode.Conflict,
            "The team roster changed during both complete paging attempts.");
    }

    public async Task<AgentOperatingStateResponse?> ReadOperatingStateAsync(
        AgentOperatingStateReadRequest request,
        CancellationToken token = default)
    {
        var response = await InvokeAsync<AgentOperatingStateReadRequest, AgentOperatingStateReadResponse>(
            PlatformCapabilities.AgentOperatingStateRead, request, token);
        return response.State;
    }

    public Task<AgentOperatingStateResponse> WriteOperatingStateAsync(
        AgentOperatingStateWriteRequest request,
        CancellationToken token = default) =>
        InvokeAsync<AgentOperatingStateWriteRequest, AgentOperatingStateResponse>(
            PlatformCapabilities.AgentOperatingStateWrite, request, token);

    public async Task<AgentOperatingState<TPayload>?> ReadOperatingStateAsync<TPayload>(
        string stateKey,
        CancellationToken token = default)
    {
        var state = await ReadOperatingStateAsync(new AgentOperatingStateReadRequest(stateKey), token);
        if (state is null)
            return null;
        try
        {
            var payload = state.Payload.Deserialize<TPayload>(JsonOptions)
                ?? throw new JsonException("The typed operating-state payload was empty.");
            return new AgentOperatingState<TPayload>(
                state.Id, state.StateKey, state.SchemaId, state.SchemaVersion, state.Status,
                state.SourceRevisions, state.ConditionCodes, state.DecisionFingerprint,
                state.OpenCommitmentCorrelations, state.AttentionReviewId, payload, state.Revision,
                state.CreatedAt, state.UpdatedAt);
        }
        catch (JsonException exception)
        {
            throw new PlatformCapabilityException(
                PlatformCapabilities.AgentOperatingStateRead,
                PlatformCapabilityErrorCode.ValidationFailed,
                "The operating-state payload does not match the requested assessment type.", exception);
        }
    }

    public async Task<AgentOperatingState<TPayload>> WriteOperatingStateAsync<TPayload>(
        WriteAgentOperatingStateRequest<TPayload> request,
        CancellationToken token = default)
    {
        var response = await WriteOperatingStateAsync(new AgentOperatingStateWriteRequest(
            request.StateKey, request.SchemaId, request.SchemaVersion, request.Status,
            request.SourceRevisions, request.ConditionCodes, request.DecisionFingerprint,
            request.OpenCommitmentCorrelations, request.AttentionReviewId,
            JsonSerializer.SerializeToElement(request.Payload, JsonOptions), request.ExpectedRevision,
            request.IdempotencyKey), token);
        var payload = response.Payload.Deserialize<TPayload>(JsonOptions)
            ?? throw new PlatformCapabilityException(
                PlatformCapabilities.AgentOperatingStateWrite,
                PlatformCapabilityErrorCode.ValidationFailed,
                "The platform returned an empty typed operating-state payload.");
        return new AgentOperatingState<TPayload>(
            response.Id, response.StateKey, response.SchemaId, response.SchemaVersion, response.Status,
            response.SourceRevisions, response.ConditionCodes, response.DecisionFingerprint,
            response.OpenCommitmentCorrelations, response.AttentionReviewId, payload, response.Revision,
            response.CreatedAt, response.UpdatedAt);
    }

    public Task<StaffingReplenishmentResponse> ProposeStaffingReplenishmentAsync(
        StaffingReplenishmentProposalRequest request,
        CancellationToken token = default) =>
        InvokeAsync<StaffingReplenishmentProposalRequest, StaffingReplenishmentResponse>(
            PlatformCapabilities.StaffingReplenishmentPropose, request, token);

    public Task<StaffingReplenishmentReadResponse> ReadStaffingReplenishmentsAsync(
        StaffingReplenishmentReadRequest request,
        CancellationToken token = default) =>
        InvokeAsync<StaffingReplenishmentReadRequest, StaffingReplenishmentReadResponse>(
            PlatformCapabilities.StaffingReplenishmentRead, request, token);

    public Task<StaffingReplenishmentResponse> DecideStaffingReplenishmentAsync(
        StaffingReplenishmentDecisionRequest request,
        CancellationToken token = default) =>
        InvokeAsync<StaffingReplenishmentDecisionRequest, StaffingReplenishmentResponse>(
            PlatformCapabilities.StaffingReplenishmentDecide, request, token);

    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string capability,
        TRequest payload,
        CancellationToken cancellationToken = default)
    {
        var result = await _tools.InvokeAsync(
            capability,
            JsonSerializer.SerializeToElement(payload, JsonOptions),
            cancellationToken);
        try
        {
            return result.Deserialize<TResponse>(JsonOptions)
                ?? throw new PlatformCapabilityException(capability, PlatformCapabilityErrorCode.ValidationFailed,
                    "The platform capability returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new PlatformCapabilityException(capability, PlatformCapabilityErrorCode.ValidationFailed,
                "The platform capability returned invalid JSON.", exception);
        }
    }

    public async Task<IReadOnlyList<AITool>> GetModelToolsAsync(
        CancellationToken cancellationToken = default)
    {
        var descriptors = await _tools.ListToolsAsync(cancellationToken);
        return descriptors
            .Where(descriptor => descriptor.ModelVisible)
            .Select(descriptor => (AITool)new RemotePlatformFunction(_tools, descriptor))
            .ToList();
    }

    /// <summary>Resolves exactly the approved model-visible bindings requested by a confined harness.</summary>
    public async Task<IReadOnlyList<AITool>> GetModelToolsAsync(
        IReadOnlyCollection<string> capabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (capabilities.Count == 0)
            return [];
        var requested = capabilities.ToHashSet(StringComparer.Ordinal);
        if (requested.Count != capabilities.Count)
            throw new PlatformCapabilityException("model-tools", PlatformCapabilityErrorCode.ValidationFailed,
                "Requested model capability names must be unique.");
        var descriptors = await _tools.ListToolsAsync(cancellationToken);
        var resolved = new List<AITool>(requested.Count);
        foreach (var capability in capabilities)
        {
            var matches = descriptors.Where(x => string.Equals(x.Capability, capability, StringComparison.Ordinal)).ToList();
            if (matches.Count != 1 || !matches[0].ModelVisible)
                throw new PlatformCapabilityException(capability, PlatformCapabilityErrorCode.Denied,
                    matches.Count == 0
                        ? "The requested capability has no approved binding."
                        : matches.Count > 1
                            ? "The requested capability has duplicate approved bindings."
                            : "The requested capability is not model-visible.");
            resolved.Add(new RemotePlatformFunction(_tools, matches[0]));
        }
        return resolved;
    }

    private sealed class RemotePlatformFunction(
        IPlatformToolInvoker tools,
        AgentToolDescriptor descriptor) : AIFunction
    {
        public override string Name => descriptor.Name;
        public override string Description => descriptor.Description;
        public override JsonElement JsonSchema => descriptor.InputSchema;
        public override JsonElement? ReturnJsonSchema => descriptor.OutputSchema;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            var payload = JsonSerializer.SerializeToElement(
                arguments.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal),
                JsonOptions);
            return await tools.InvokeAsync(descriptor.Capability, payload, cancellationToken);
        }
    }
}

public sealed class PlatformMemoryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPlatformToolInvoker _tools;

    internal PlatformMemoryClient(IPlatformToolInvoker tools) => _tools = tools;

    public async Task<T> ExecuteAsync<T>(
        string access,
        string operation,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var capability = access switch
        {
            "query" => "platform.memory.query.v1",
            "write" => "platform.memory.write.v1",
            "manage" => "platform.memory.manage.v1",
            "export" => "platform.memory.export.v1",
            _ => throw new ArgumentOutOfRangeException(nameof(access), "Memory access must be query, write, manage, or export.")
        };
        var result = await _tools.InvokeAsync(
            capability,
            JsonSerializer.SerializeToElement(new
            {
                operation,
                payload = JsonSerializer.SerializeToElement(payload, JsonOptions)
            }, JsonOptions),
            cancellationToken);
        return result.Deserialize<T>(JsonOptions)
            ?? throw new PlatformCapabilityException(
                capability,
                PlatformCapabilityErrorCode.ValidationFailed,
                "The memory capability returned an empty response.");
    }
}

public sealed class AgentPlatformAccessor
{
    private PlatformCapabilityClient? _current;

    public PlatformCapabilityClient Current =>
        Volatile.Read(ref _current)
        ?? throw new InvalidOperationException("The agent runtime has not established a platform session.");

    internal void SetCurrent(PlatformCapabilityClient platform) =>
        Volatile.Write(ref _current, platform);
}

public sealed class PlatformCapabilityException : Exception
{
    public PlatformCapabilityException(
        string capability,
        PlatformCapabilityErrorCode code,
        string message,
        Exception? inner = null,
        string? failureCode = null,
        bool? retryable = null)
        : base(message, inner)
    {
        Capability = capability;
        Code = code;
        FailureCode = failureCode;
        Retryable = retryable;
    }

    public string Capability { get; }
    public PlatformCapabilityErrorCode Code { get; }
    public string? FailureCode { get; }
    public bool? Retryable { get; }

}
