using System.Text.Json;
using CSweet.Agent.Contracts.Grpc;
using Google.Protobuf;

namespace CSweet.Agent.SDK;

/// <summary>Typed, broker-governed access to authoritative C-Sweet platform services.</summary>
public sealed class PlatformCapabilityClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IAgentBrokerClient _broker;

    public PlatformCapabilityClient(IAgentBrokerClient broker) => _broker = broker;

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

    public Task<HiringRecommendationResponse> UpsertHiringRecommendationAsync(UpsertHiringRecommendationRequest request, CancellationToken token = default) =>
        InvokeAsync<UpsertHiringRecommendationRequest, HiringRecommendationResponse>(PlatformCapabilities.HiringRecommendationUpsert, request, token);

    public Task<HiringWorkflowResponse> StageHiringWorkflowAsync(StageHiringWorkflowRequest request, CancellationToken token = default) =>
        InvokeAsync<StageHiringWorkflowRequest, HiringWorkflowResponse>(PlatformCapabilities.HiringWorkflowStage, request, token);

    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string capability,
        TRequest payload,
        CancellationToken cancellationToken = default)
    {
        var result = await _broker.InvokeCapabilityAsync(new RequestCapability
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Capability = capability,
            ContentType = "application/json",
            Payload = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions))
        }, cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            throw PlatformCapabilityException.FromResult(capability, result);
        }

        try
        {
            return JsonSerializer.Deserialize<TResponse>(result.Payload.Span, JsonOptions)
                ?? throw new PlatformCapabilityException(capability, PlatformCapabilityErrorCode.ValidationFailed,
                    "The platform capability returned an empty response.");
        }
        catch (JsonException exception)
        {
            throw new PlatformCapabilityException(capability, PlatformCapabilityErrorCode.ValidationFailed,
                "The platform capability returned invalid JSON.", exception);
        }
    }
}

public sealed class PlatformCapabilityException : Exception
{
    public PlatformCapabilityException(string capability, PlatformCapabilityErrorCode code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Capability = capability;
        Code = code;
    }

    public string Capability { get; }
    public PlatformCapabilityErrorCode Code { get; }

    internal static PlatformCapabilityException FromResult(string capability, CapabilityResult result)
    {
        try
        {
            var error = result.Payload.Length == 0
                ? null
                : JsonSerializer.Deserialize<PlatformCapabilityError>(result.Payload.Span, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return new PlatformCapabilityException(capability, error?.Code ?? InferCode(result.Error), error?.Message ?? result.Error);
        }
        catch (JsonException)
        {
            return new PlatformCapabilityException(capability, InferCode(result.Error), result.Error);
        }
    }

    private static PlatformCapabilityErrorCode InferCode(string error)
    {
        if (error.Contains("grant", StringComparison.OrdinalIgnoreCase) || error.Contains("denied", StringComparison.OrdinalIgnoreCase))
            return PlatformCapabilityErrorCode.Denied;
        if (error.Contains("not found", StringComparison.OrdinalIgnoreCase)) return PlatformCapabilityErrorCode.NotFound;
        if (error.Contains("budget", StringComparison.OrdinalIgnoreCase)) return PlatformCapabilityErrorCode.BudgetExceeded;
        if (error.Contains("revision", StringComparison.OrdinalIgnoreCase) || error.Contains("conflict", StringComparison.OrdinalIgnoreCase))
            return PlatformCapabilityErrorCode.Conflict;
        return PlatformCapabilityErrorCode.Unavailable;
    }
}
