using System.Text.Json;

namespace CSweet.Agent.SDK;

public static class InfrastructureCapabilityNames
{
    public const string EnvironmentRead = "platform.infrastructure.environment.read.v1";
    public const string StateWrite = "platform.infrastructure.state.write.v1";
    public const string ChangePropose = "platform.infrastructure.change.propose.v1";
    public const string ChangeRead = "platform.infrastructure.change.read.v1";
    public const string OperationExecute = "platform.infrastructure.operation.execute.v1";
    public const string Reconcile = "platform.infrastructure.reconcile.v1";
    public const string DeploymentContractPublish = "platform.infrastructure.deployment-contract.publish.v1";
    public const string FileTransfer = "platform.infrastructure.file-transfer.v1";
}

public sealed record InfrastructureEnvironment(
    Guid Id,
    Guid OrganizationId,
    string Provider,
    string AccountReference,
    string Environment,
    long Revision,
    Guid? DesiredStateRevisionId,
    Guid? ObservedStateRevisionId,
    DateTimeOffset? NextReconciliationAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record InfrastructureEnvironmentReadRequest(Guid? EnvironmentId = null, string? Provider = null);

public sealed record InfrastructureStateWriteRequest(
    Guid EnvironmentId,
    string Kind,
    string SchemaId,
    int SchemaVersion,
    JsonElement State,
    long ExpectedEnvironmentRevision,
    string IdempotencyKey);

public sealed record InfrastructureStateRevision(
    Guid Id,
    Guid EnvironmentId,
    string Kind,
    string SchemaId,
    int SchemaVersion,
    JsonElement State,
    string ContentHash,
    long Revision,
    DateTimeOffset CreatedAt);

public sealed record InfrastructureFiscalImpact(
    bool HasFiscalImpact,
    decimal? MinimumAmount,
    decimal? MaximumAmount,
    string Currency,
    bool Recurring,
    string? RenewalCadence,
    string BudgetStatus);

public sealed record InfrastructureOperation(
    string Capability,
    JsonElement Input,
    string Effect,
    string IdempotencyKey);

public sealed record InfrastructureChangeProposalRequest(
    Guid EnvironmentId,
    Guid? DesiredStateRevisionId,
    Guid? ObservedStateRevisionId,
    string Summary,
    IReadOnlyList<InfrastructureOperation> Operations,
    InfrastructureFiscalImpact FiscalImpact,
    string RollbackPlan,
    DateTimeOffset ExpiresAt,
    string IdempotencyKey);

public sealed record InfrastructureApprovalStage(
    int Sequence,
    string Kind,
    Guid ApproverOrganizationUserId,
    string Status,
    Guid? DecidedByOrganizationUserId,
    string? Comment,
    DateTimeOffset? DecidedAt);

public sealed record InfrastructureChangeSet(
    Guid Id,
    Guid EnvironmentId,
    string Summary,
    string PayloadHash,
    string Status,
    IReadOnlyList<InfrastructureOperation> Operations,
    InfrastructureFiscalImpact FiscalImpact,
    IReadOnlyList<InfrastructureApprovalStage> ApprovalRoute,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record InfrastructureChangeReadRequest(Guid? ChangeSetId = null, Guid? EnvironmentId = null);

public sealed record InfrastructureOperationExecuteRequest(Guid ChangeSetId, string ExpectedPayloadHash);

public sealed record InfrastructureOperationReceipt(
    Guid Id,
    Guid ChangeSetId,
    string Capability,
    string Status,
    JsonElement SanitizedResult,
    string? BeforeStateHash,
    string? AfterStateHash,
    string CorrelationId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

public sealed record InfrastructureReconcileRequest(Guid EnvironmentId, bool Force = false);

public sealed record InfrastructureReconciliationReport(
    Guid Id,
    Guid EnvironmentId,
    string Status,
    IReadOnlyList<string> Drift,
    IReadOnlyList<string> ExpiryRisks,
    IReadOnlyList<string> RequiredActions,
    DateTimeOffset ObservedAt,
    DateTimeOffset NextReconciliationAt);

public sealed record InfrastructureDeploymentContractPublishRequest(
    Guid EnvironmentId,
    string Domain,
    string HostingTarget,
    IReadOnlyList<string> Endpoints,
    IReadOnlyList<string> DnsExpectations,
    IReadOnlyList<string> ArtifactRequirements,
    IReadOnlyList<string> BrokeredCredentialReferences,
    string IdempotencyKey);

public sealed record InfrastructureDeploymentContract(
    Guid Id,
    Guid EnvironmentId,
    int Version,
    string Domain,
    string HostingTarget,
    IReadOnlyList<string> Endpoints,
    IReadOnlyList<string> DnsExpectations,
    IReadOnlyList<string> ArtifactRequirements,
    IReadOnlyList<string> BrokeredCredentialReferences,
    string ContentHash,
    DateTimeOffset CreatedAt);

public sealed record InfrastructureFileTransferRequest(
    string Target,
    string Operation,
    string Host,
    string RelativePath,
    byte[]? Content,
    string? ExpectedContentHash,
    Guid? ChangeSetId,
    string IdempotencyKey);

public sealed record InfrastructureFileTransferResponse(
    string Status,
    string Host,
    string RelativePath,
    long? Length,
    string? ContentHash,
    string? HostKeyFingerprint);

public sealed class PlatformInfrastructureClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPlatformToolInvoker _tools;

    internal PlatformInfrastructureClient(IPlatformToolInvoker tools) => _tools = tools;

    public Task<IReadOnlyList<InfrastructureEnvironment>> ReadEnvironmentsAsync(
        InfrastructureEnvironmentReadRequest request, CancellationToken token = default) =>
        InvokeAsync<InfrastructureEnvironmentReadRequest, IReadOnlyList<InfrastructureEnvironment>>(
            InfrastructureCapabilityNames.EnvironmentRead, request, token);

    public Task<InfrastructureStateRevision> WriteStateAsync(
        InfrastructureStateWriteRequest request, CancellationToken token = default) =>
        InvokeAsync<InfrastructureStateWriteRequest, InfrastructureStateRevision>(
            InfrastructureCapabilityNames.StateWrite, request, token);

    public Task<InfrastructureChangeSet> ProposeChangeAsync(
        InfrastructureChangeProposalRequest request, CancellationToken token = default) =>
        InvokeAsync<InfrastructureChangeProposalRequest, InfrastructureChangeSet>(
            InfrastructureCapabilityNames.ChangePropose, request, token);

    public Task<IReadOnlyList<InfrastructureChangeSet>> ReadChangesAsync(
        InfrastructureChangeReadRequest request, CancellationToken token = default) =>
        InvokeAsync<InfrastructureChangeReadRequest, IReadOnlyList<InfrastructureChangeSet>>(
            InfrastructureCapabilityNames.ChangeRead, request, token);

    public Task<IReadOnlyList<InfrastructureOperationReceipt>> ExecuteApprovedChangeAsync(
        InfrastructureOperationExecuteRequest request, CancellationToken token = default) =>
        InvokeAsync<InfrastructureOperationExecuteRequest, IReadOnlyList<InfrastructureOperationReceipt>>(
            InfrastructureCapabilityNames.OperationExecute, request, token);

    public Task<InfrastructureReconciliationReport> ReconcileAsync(
        InfrastructureReconcileRequest request, CancellationToken token = default) =>
        InvokeAsync<InfrastructureReconcileRequest, InfrastructureReconciliationReport>(
            InfrastructureCapabilityNames.Reconcile, request, token);

    public Task<InfrastructureDeploymentContract> PublishDeploymentContractAsync(
        InfrastructureDeploymentContractPublishRequest request, CancellationToken token = default) =>
        InvokeAsync<InfrastructureDeploymentContractPublishRequest, InfrastructureDeploymentContract>(
            InfrastructureCapabilityNames.DeploymentContractPublish, request, token);

    public Task<InfrastructureFileTransferResponse> TransferFileAsync(
        InfrastructureFileTransferRequest request, CancellationToken token = default) =>
        InvokeAsync<InfrastructureFileTransferRequest, InfrastructureFileTransferResponse>(
            InfrastructureCapabilityNames.FileTransfer, request, token);

    private async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string capability, TRequest request, CancellationToken token)
    {
        var result = await _tools.InvokeAsync(capability, JsonSerializer.SerializeToElement(request, JsonOptions), token);
        return result.Deserialize<TResponse>(JsonOptions) ?? throw new PlatformCapabilityException(
            capability, PlatformCapabilityErrorCode.ValidationFailed, "The infrastructure capability returned an empty response.");
    }
}
