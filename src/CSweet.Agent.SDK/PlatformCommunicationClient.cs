using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

public static class AgentCoordinationEvents
{
    public const string TurnRequested = "com.csweet.agent.coordination.turn-requested.v1";
}

public static class CommunicationEvents
{
    public const string MessageReceived = "com.csweet.user.message.received.v1";
    public const string MessageMentioned = "com.csweet.communication.message.mentioned.v1";
}

/// <summary>Broker-authenticated context keys supplied with a received conversation turn.</summary>
public static class CommunicationMessageContextKeys
{
    public const string SenderOrganizationUserId = "senderOrganizationUserId";
    public const string SenderDisplayName = "senderDisplayName";
    public const string SenderEmployeeType = "senderEmployeeType";
    public const string SenderRole = "senderRole";
}

/// <summary>The stable payload delivered for a human or agent conversation turn.</summary>
public sealed record CommunicationMessageReceivedEvent(
    Guid ProviderProfileId,
    string ConversationId,
    string UserId,
    string Message,
    IReadOnlyDictionary<string, string>? Context,
    Guid TurnId = default,
    int Attempt = 0,
    Guid MessageId = default)
{
    public IReadOnlyList<CommunicationAttachment> Attachments { get; init; } = [];
    public AgentWorkContext? WorkContext { get; init; }
}

/// <summary>
/// A sanitized, immutable reference to media attached to retained conversation history.
/// The reference never contains storage paths or raw file bytes.
/// </summary>
public sealed record CommunicationAttachment(
    Guid Id,
    Guid MessageId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256);

public static class AgentCoordinationDispositions
{
    public const string Continue = "Continue";
    public const string Completed = "Completed";
    public const string Blocked = "Blocked";
}

public static class AgentCoordinationStatuses
{
    public const string Active = "Active";
    public const string Summarizing = "Summarizing";
    public const string Completed = "Completed";
    public const string Blocked = "Blocked";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";
}

public sealed record CommunicationParticipant(
    Guid OrganizationUserId,
    string DisplayName,
    string EmployeeType,
    string Role);

public sealed record CommunicationChat(
    Guid Id,
    string Title,
    string? Description,
    bool IsDirect,
    bool IsPrivate,
    bool IsDeletionProtected,
    bool CanManage,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CommunicationParticipant> Participants,
    string? LastMessage,
    DateTimeOffset? LastMessageAt,
    int UnreadCount)
{
    public AgentWorkContext? WorkContext { get; init; }
    public Guid? WorkstreamId { get; init; }
    public Guid? TeamId { get; init; }
}

public sealed record CommunicationMessage(
    Guid Id,
    long Sequence,
    Guid ChatId,
    Guid? SenderOrganizationUserId,
    string SenderDisplayName,
    string SenderEmployeeType,
    string Content,
    DateTimeOffset CreatedAt,
    Guid? ChatTurnId = null,
    Guid? CoordinationSessionId = null,
    IReadOnlyList<CommunicationMessageMention>? Mentions = null)
{
    public IReadOnlyList<CommunicationAttachment> Attachments { get; init; } = [];
    public AgentWorkContext? WorkContext { get; init; }
}

public sealed record CommunicationMessageMention(
    Guid OrganizationUserId,
    string DisplayName,
    string EmployeeType,
    int Offset,
    int Length,
    string DisplayText);

public sealed record CommunicationMessageMentionInput(
    Guid OrganizationUserId,
    int Offset,
    int Length);

public sealed record CommunicationMessageMentionedEvent(
    Guid MentionId,
    Guid MessageId,
    Guid ChatId,
    Guid MentionedOrganizationUserId,
    Guid? SenderOrganizationUserId,
    string SenderDisplayName,
    string Content,
    int Offset,
    int Length,
    DateTimeOffset CreatedAt);

public sealed record CommunicationHub(
    Guid CurrentOrganizationUserId,
    Guid ViewedOrganizationUserId,
    bool IsReadOnlyPerspective,
    bool CanManageChats,
    IReadOnlyList<CommunicationChat> Chats);

public sealed record CreateCommunicationChat(
    string? Title,
    string? Description,
    bool IsDirect,
    bool IsPrivate,
    IReadOnlyList<Guid> ParticipantOrganizationUserIds,
    IReadOnlyList<Guid>? AudienceRoleIds = null,
    IReadOnlyList<Guid>? AudienceWorkstreamIds = null)
{
    public Guid? WorkstreamId { get; init; }
    public Guid? TeamId { get; init; }
}

public sealed record ModifyCommunicationChat(
    Guid ChatId,
    string Title,
    string? Description,
    bool IsPrivate,
    IReadOnlyList<Guid> ParticipantOrganizationUserIds,
    IReadOnlyList<Guid>? AudienceRoleIds = null,
    IReadOnlyList<Guid>? AudienceWorkstreamIds = null);

public sealed record CommunicationAction(
    bool Succeeded,
    string? ErrorCode,
    string Message,
    CommunicationChat? Chat = null);

public sealed record CommunicationMessages(IReadOnlyList<CommunicationMessage> Messages);

public sealed record AgentMessageDispatchReceipt(
    Guid ChatId,
    Guid MessageId,
    Guid RecipientOrganizationUserId,
    Guid RecipientChatTurnId,
    DateTimeOffset DispatchedAt);

public sealed record DirectMessageDispatchReceipt(
    Guid ChatId,
    Guid MessageId,
    Guid RecipientOrganizationUserId,
    string RecipientEmployeeType,
    Guid? RecipientChatTurnId,
    DateTimeOffset DispatchedAt);

public sealed record StartAgentCoordinationRequest(
    Guid TargetOrganizationUserId,
    string Subject,
    string Objective,
    IReadOnlyList<string> SuccessCriteria,
    string InitialMessage,
    Guid SourceConversationId,
    Guid SourceChatTurnId,
    Guid SourceMessageId,
    string IdempotencyKey,
    AgentCoordinationArtifactSubmission? Artifact = null)
{
    public AgentWorkContext? WorkContext { get; init; }
}

public sealed record StartWorkItemCoordinationRequest(
    Guid TargetOrganizationUserId,
    Guid BoardId,
    Guid ItemId,
    Guid SprintExecutionId,
    Guid StageExecutionId,
    long AssignmentRevision,
    string Subject,
    string Objective,
    IReadOnlyList<string> SuccessCriteria,
    string InitialMessage,
    string IdempotencyKey,
    AgentCoordinationArtifactSubmission? Artifact = null);

public sealed record StartBoardCoordinationRequest(
    Guid TargetOrganizationUserId,
    Guid BoardId,
    string Subject,
    string Objective,
    IReadOnlyList<string> SuccessCriteria,
    string InitialMessage,
    string IdempotencyKey,
    AgentCoordinationArtifactSubmission? Artifact = null);

public sealed record AgentCoordinationWorkSource(
    Guid BoardId,
    Guid ItemId,
    Guid SprintExecutionId,
    Guid StageExecutionId,
    long AssignmentRevision);

public sealed record AgentCoordinationBoardSource(Guid BoardId);

public sealed record RespondToAgentCoordinationRequest(
    Guid SessionId,
    long ExpectedRevision,
    int ExpectedTurnOrdinal,
    string Disposition,
    string Content,
    string IdempotencyKey,
    AgentCoordinationArtifactSubmission? Artifact = null);

public sealed record ReadAgentCoordinationRequest(Guid SessionId);

public sealed record ListAgentCoordinationRequest(Guid? ChatId = null, bool ActiveOnly = false);

public sealed record AgentCoordinationSessions(IReadOnlyList<AgentCoordinationSession> Sessions);

public sealed record ResumeAgentCoordinationRequest(
    Guid SessionId,
    long ExpectedRevision,
    string Reason,
    string IdempotencyKey);

public sealed record CancelAgentCoordinationRequest(
    Guid SessionId,
    long ExpectedRevision,
    string Reason,
    string IdempotencyKey);

public sealed record AgentCoordinationParticipant(
    Guid OrganizationUserId,
    Guid AgentInstallationId,
    string DisplayName,
    string Role);

public sealed record AgentCoordinationTurn(
    Guid Id,
    int Ordinal,
    Guid SpeakerOrganizationUserId,
    string Disposition,
    string Content,
    DateTimeOffset CreatedAt,
    AgentCoordinationArtifact? Artifact = null);

/// <summary>A bounded, caller-authored structured artifact attached to one coordination turn.</summary>
public sealed record AgentCoordinationArtifactSubmission(
    string Type,
    string SchemaVersion,
    string Key,
    int PageOrdinal,
    bool IsFinalPage,
    JsonElement Payload);

/// <summary>A durable coordination artifact whose digest was computed by the platform.</summary>
public sealed record AgentCoordinationArtifact(
    string Type,
    string SchemaVersion,
    string Key,
    int PageOrdinal,
    bool IsFinalPage,
    JsonElement Payload,
    string Digest);

public sealed record AgentCoordinationSession(
    Guid Id,
    Guid ConversationId,
    Guid SourceConversationId,
    Guid SourceChatTurnId,
    Guid SourceMessageId,
    AgentCoordinationParticipant Initiator,
    AgentCoordinationParticipant Target,
    string Subject,
    string Objective,
    IReadOnlyList<string> SuccessCriteria,
    string Status,
    long Revision,
    int NextTurnOrdinal,
    Guid? CurrentOrganizationUserId,
    bool IsFinalization,
    string? FinalSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AgentCoordinationTurn> Turns)
{
    public string SourceKind { get; init; } = "Chat";
    public AgentCoordinationWorkSource? WorkSource { get; init; }
    public AgentCoordinationBoardSource? BoardSource { get; init; }
    public int? MaximumTurns { get; init; }
    public AgentWorkContext? WorkContext { get; init; }
}

public sealed record AgentCoordinationTurnRequest(
    Guid SessionId,
    long ExpectedRevision,
    int TurnOrdinal,
    string Subject,
    string Objective,
    IReadOnlyList<string> SuccessCriteria,
    AgentCoordinationParticipant Self,
    AgentCoordinationParticipant Counterpart,
    bool IsFinalization,
    IReadOnlyList<AgentCoordinationTurn> Transcript)
{
    public string SourceKind { get; init; } = "Chat";
    public AgentCoordinationWorkSource? WorkSource { get; init; }
    public AgentCoordinationBoardSource? BoardSource { get; init; }
    public int? MaximumTurns { get; init; }
    public AgentWorkContext? WorkContext { get; init; }
}

public sealed record AgentCoordinationTurnResult(
    string Disposition,
    string Content,
    AgentCoordinationArtifactSubmission? Artifact = null)
{
    public static AgentCoordinationTurnResult Continue(
        string content,
        AgentCoordinationArtifactSubmission? artifact = null) =>
        new(AgentCoordinationDispositions.Continue, content, artifact);

    public static AgentCoordinationTurnResult Completed(
        string content,
        AgentCoordinationArtifactSubmission? artifact = null) =>
        new(AgentCoordinationDispositions.Completed, content, artifact);

    public static AgentCoordinationTurnResult Blocked(
        string content,
        AgentCoordinationArtifactSubmission? artifact = null) =>
        new(AgentCoordinationDispositions.Blocked, content, artifact);
}

/// <summary>Typed, grant-governed access to C-Sweet communications and agent coordination.</summary>
public sealed class PlatformCommunicationClient
{
    private readonly PlatformCapabilityClient _platform;

    internal PlatformCommunicationClient(PlatformCapabilityClient platform) => _platform = platform;

    public Task<CommunicationHub> ReadHubAsync(CancellationToken token = default) =>
        _platform.InvokeAsync<object, CommunicationHub>(CommunicationCapabilities.ChatRead, new { }, token);

    public Task<CommunicationMessages> ReadChatAsync(Guid chatId, CancellationToken token = default) =>
        _platform.InvokeAsync<object, CommunicationMessages>(
            CommunicationCapabilities.ChatRead, new { chatId }, token);

    public async Task<CommunicationChat> CreateChatAsync(
        CreateCommunicationChat request,
        CancellationToken token = default)
    {
        var action = await _platform.InvokeAsync<CreateCommunicationChat, CommunicationAction>(
            CommunicationCapabilities.ChatCreate, request, token);
        return action.Chat ?? throw Invalid(CommunicationCapabilities.ChatCreate,
            "The communication platform did not return the created chat.");
    }

    public async Task<CommunicationChat> ModifyChatAsync(
        ModifyCommunicationChat request,
        CancellationToken token = default)
    {
        var action = await _platform.InvokeAsync<ModifyCommunicationChat, CommunicationAction>(
            CommunicationCapabilities.ChatModify, request, token);
        return action.Chat ?? throw Invalid(CommunicationCapabilities.ChatModify,
            "The communication platform did not return the modified chat.");
    }

    public Task<CommunicationAction> ArchiveChatAsync(Guid chatId, CancellationToken token = default) =>
        _platform.InvokeAsync<object, CommunicationAction>(
            CommunicationCapabilities.ChatDelete, new { chatId }, token);

    public Task<CommunicationMessage> SendMessageAsync(
        Guid chatId,
        string content,
        string? idempotencyKey = null,
        CancellationToken token = default) =>
        _platform.InvokeAsync<object, CommunicationMessage>(
            CommunicationCapabilities.MessageSend,
            new { chatId, content, idempotencyKey },
            token);

    public Task<CommunicationMessage> SendMessageAsync(
        Guid chatId,
        string content,
        IReadOnlyList<Guid> attachmentMediaAssetIds,
        string? idempotencyKey = null,
        CancellationToken token = default) =>
        _platform.InvokeAsync<object, CommunicationMessage>(
            CommunicationCapabilities.MessageSend,
            new { chatId, content, idempotencyKey, attachmentMediaAssetIds },
            token);

    public Task<CommunicationMessage> SendMessageAsync(
        Guid chatId,
        string content,
        IReadOnlyList<CommunicationMessageMentionInput> mentions,
        string? idempotencyKey = null,
        CancellationToken token = default) =>
        _platform.InvokeAsync<object, CommunicationMessage>(
            CommunicationCapabilities.MessageSend,
            new { chatId, content, idempotencyKey, mentions },
            token);

    public async Task<DirectMessageDispatchReceipt> SendDirectMessageAsync(
        Guid recipientOrganizationUserId,
        string content,
        string idempotencyKey,
        CancellationToken token = default)
        => await SendDirectMessageAsync(recipientOrganizationUserId, content, idempotencyKey, null, token);

    public async Task<DirectMessageDispatchReceipt> SendDirectMessageAsync(
        Guid recipientOrganizationUserId,
        string content,
        string idempotencyKey,
        AgentWorkContext? workContext,
        CancellationToken token = default)
    {
        var chat = await CreateChatAsync(new CreateCommunicationChat(
            null,
            "Private direct conversation.",
            true,
            true,
            [recipientOrganizationUserId],
            AudienceWorkstreamIds: workContext?.WorkstreamId is { } workstreamId ? [workstreamId] : null)
        {
            WorkstreamId = workContext?.WorkstreamId,
            TeamId = workContext?.TeamId
        }, token);
        var recipient = chat.Participants.SingleOrDefault(x =>
            x.OrganizationUserId == recipientOrganizationUserId);
        if (recipient is null)
            throw Invalid(CommunicationCapabilities.ChatCreate,
                "The requested direct-message recipient is not an active participant.");
        var message = await SendMessageAsync(chat.Id, content, idempotencyKey, token);
        if (string.Equals(recipient.EmployeeType, "Agent", StringComparison.OrdinalIgnoreCase) &&
            message.ChatTurnId is not { } turnId)
            throw Invalid(CommunicationCapabilities.MessageSend,
                "The agent message was persisted but no recipient turn was created.");
        return new DirectMessageDispatchReceipt(
            chat.Id, message.Id, recipientOrganizationUserId, recipient.EmployeeType,
            message.ChatTurnId, message.CreatedAt);
    }

    public async Task<AgentMessageDispatchReceipt> SendDirectAgentMessageAsync(
        Guid recipientOrganizationUserId,
        string content,
        string idempotencyKey,
        CancellationToken token = default)
    {
        var result = await SendDirectMessageAsync(
            recipientOrganizationUserId, content, idempotencyKey, token);
        if (!string.Equals(result.RecipientEmployeeType, "Agent", StringComparison.OrdinalIgnoreCase) ||
            result.RecipientChatTurnId is not { } turnId || turnId == Guid.Empty)
            throw Invalid(CommunicationCapabilities.MessageSend,
                "The requested direct-message recipient is not an active agent participant.");
        return new AgentMessageDispatchReceipt(
            result.ChatId, result.MessageId, recipientOrganizationUserId, turnId,
            result.DispatchedAt);
    }

    public Task<AgentCoordinationSession> StartCoordinationAsync(
        StartAgentCoordinationRequest request,
        CancellationToken token = default) =>
        _platform.InvokeAsync<StartAgentCoordinationRequest, AgentCoordinationSession>(
            CommunicationCapabilities.CoordinationStart, request, token);

    public Task<AgentCoordinationSession> StartWorkItemCoordinationAsync(
        StartWorkItemCoordinationRequest request,
        CancellationToken token = default) =>
        _platform.InvokeAsync<StartWorkItemCoordinationRequest, AgentCoordinationSession>(
            CommunicationCapabilities.CoordinationStartWork, request, token);

    public Task<AgentCoordinationSession> StartBoardCoordinationAsync(
        StartBoardCoordinationRequest request,
        CancellationToken token = default) =>
        _platform.InvokeAsync<StartBoardCoordinationRequest, AgentCoordinationSession>(
            CommunicationCapabilities.CoordinationStartBoard, request, token);

    public async Task<AgentCoordinationSession> RespondToCoordinationAsync(
        RespondToAgentCoordinationRequest request,
        CancellationToken token = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await _platform.InvokeAsync<RespondToAgentCoordinationRequest, AgentCoordinationSession>(
                    CommunicationCapabilities.CoordinationRespond, request, token);
            }
            catch (Exception exception) when (attempt < 2 && IsTransientCoordinationFailure(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), token);
            }
        }
    }

    public Task<AgentCoordinationSession> ReadCoordinationAsync(
        Guid sessionId,
        CancellationToken token = default) =>
        _platform.InvokeAsync<ReadAgentCoordinationRequest, AgentCoordinationSession>(
            CommunicationCapabilities.CoordinationRead, new(sessionId), token);

    public Task<AgentCoordinationSessions> ListCoordinationAsync(
        Guid? chatId = null,
        bool activeOnly = false,
        CancellationToken token = default) =>
        _platform.InvokeAsync<ListAgentCoordinationRequest, AgentCoordinationSessions>(
            CommunicationCapabilities.CoordinationList, new(chatId, activeOnly), token);

    public Task<AgentCoordinationSession> ResumeCoordinationAsync(
        Guid sessionId,
        long expectedRevision,
        string reason,
        string idempotencyKey,
        CancellationToken token = default) =>
        _platform.InvokeAsync<ResumeAgentCoordinationRequest, AgentCoordinationSession>(
            CommunicationCapabilities.CoordinationResume,
            new(sessionId, expectedRevision, reason, idempotencyKey), token);

    public Task<AgentCoordinationSession> CancelCoordinationAsync(
        Guid sessionId,
        long expectedRevision,
        string reason,
        string idempotencyKey,
        CancellationToken token = default) =>
        _platform.InvokeAsync<CancelAgentCoordinationRequest, AgentCoordinationSession>(
            CommunicationCapabilities.CoordinationCancel,
            new(sessionId, expectedRevision, reason, idempotencyKey), token);

    private static PlatformCapabilityException Invalid(string capability, string message) =>
        new(capability, PlatformCapabilityErrorCode.ValidationFailed, message);

    private static bool IsTransientCoordinationFailure(Exception exception) =>
        exception is HttpRequestException or TimeoutException ||
        exception is PlatformCapabilityException { Retryable: true };
}
