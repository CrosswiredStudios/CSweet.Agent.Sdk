using System.Text.Json;

namespace CSweet.Agent.SDK;

public static class AgentCoordinationEvents
{
    public const string TurnRequested = "com.csweet.agent.coordination.turn-requested.v1";
}

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
    int UnreadCount);

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
    Guid? CoordinationSessionId = null);

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
    IReadOnlyList<Guid>? AudienceWorkstreamIds = null);

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

public sealed record StartAgentCoordinationRequest(
    Guid TargetOrganizationUserId,
    string Subject,
    string Objective,
    IReadOnlyList<string> SuccessCriteria,
    string InitialMessage,
    Guid SourceConversationId,
    Guid SourceChatTurnId,
    Guid SourceMessageId,
    string IdempotencyKey);

public sealed record RespondToAgentCoordinationRequest(
    Guid SessionId,
    long ExpectedRevision,
    int ExpectedTurnOrdinal,
    string Disposition,
    string Content,
    string IdempotencyKey);

public sealed record ReadAgentCoordinationRequest(Guid SessionId);

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
    DateTimeOffset CreatedAt);

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
    IReadOnlyList<AgentCoordinationTurn> Turns);

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
    IReadOnlyList<AgentCoordinationTurn> Transcript);

public sealed record AgentCoordinationTurnResult(string Disposition, string Content)
{
    public static AgentCoordinationTurnResult Continue(string content) =>
        new(AgentCoordinationDispositions.Continue, content);

    public static AgentCoordinationTurnResult Completed(string content) =>
        new(AgentCoordinationDispositions.Completed, content);

    public static AgentCoordinationTurnResult Blocked(string content) =>
        new(AgentCoordinationDispositions.Blocked, content);
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

    public async Task<AgentMessageDispatchReceipt> SendDirectAgentMessageAsync(
        Guid recipientOrganizationUserId,
        string content,
        string idempotencyKey,
        CancellationToken token = default)
    {
        var chat = await CreateChatAsync(new CreateCommunicationChat(
            null,
            "Private agent coordination conversation.",
            true,
            true,
            [recipientOrganizationUserId]), token);
        var recipient = chat.Participants.SingleOrDefault(x =>
            x.OrganizationUserId == recipientOrganizationUserId &&
            string.Equals(x.EmployeeType, "Agent", StringComparison.OrdinalIgnoreCase));
        if (recipient is null)
            throw Invalid(CommunicationCapabilities.ChatCreate,
                "The requested direct-message recipient is not an active agent participant.");
        var message = await SendMessageAsync(chat.Id, content, idempotencyKey, token);
        if (message.ChatTurnId is not { } turnId || turnId == Guid.Empty)
            throw Invalid(CommunicationCapabilities.MessageSend,
                "The message was persisted but no recipient agent turn was created.");
        return new AgentMessageDispatchReceipt(
            chat.Id, message.Id, recipientOrganizationUserId, turnId, message.CreatedAt);
    }

    public Task<AgentCoordinationSession> StartCoordinationAsync(
        StartAgentCoordinationRequest request,
        CancellationToken token = default) =>
        _platform.InvokeAsync<StartAgentCoordinationRequest, AgentCoordinationSession>(
            CommunicationCapabilities.CoordinationStart, request, token);

    public Task<AgentCoordinationSession> RespondToCoordinationAsync(
        RespondToAgentCoordinationRequest request,
        CancellationToken token = default) =>
        _platform.InvokeAsync<RespondToAgentCoordinationRequest, AgentCoordinationSession>(
            CommunicationCapabilities.CoordinationRespond, request, token);

    public Task<AgentCoordinationSession> ReadCoordinationAsync(
        Guid sessionId,
        CancellationToken token = default) =>
        _platform.InvokeAsync<ReadAgentCoordinationRequest, AgentCoordinationSession>(
            CommunicationCapabilities.CoordinationRead, new(sessionId), token);

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
}
