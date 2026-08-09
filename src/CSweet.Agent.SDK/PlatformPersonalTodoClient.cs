using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK.WorkManagement;

/// <summary>Grant-governed access to the caller's and direct reports' personal work queues.</summary>
public sealed class PlatformPersonalTodoClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IPlatformToolInvoker _tools;

    internal PlatformPersonalTodoClient(IPlatformToolInvoker tools) => _tools = tools;

    public Task<PersonalTodoDirectory> ListAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync<object, PersonalTodoDirectory>(PersonalTodoCapabilities.Read, new { }, cancellationToken);

    public Task<PersonalTodoItem> AddAsync(
        AddPersonalTodoItemRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<AddPersonalTodoItemRequest, PersonalTodoItem>(
            PersonalTodoCapabilities.Add, request, cancellationToken);

    public Task<PersonalTodoItem> ReorderAsync(
        ReorderPersonalTodoItemRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<ReorderPersonalTodoItemRequest, PersonalTodoItem>(
            PersonalTodoCapabilities.Reorder, request, cancellationToken);

    public Task<PersonalTodoItem> RequeueAsync(
        RequeuePersonalTodoItemRequest request, CancellationToken cancellationToken = default) =>
        InvokeAsync<RequeuePersonalTodoItemRequest, PersonalTodoItem>(
            PersonalTodoCapabilities.Requeue, request, cancellationToken);

    internal Task<PersonalTodoClaim> ClaimAsync(Guid eventId, CancellationToken cancellationToken) =>
        InvokeAsync<ClaimPersonalTodoItemRequest, PersonalTodoClaim>(
            PersonalTodoCapabilities.Claim,
            new(eventId, $"personal-todo-claim:{eventId:N}"), cancellationToken);

    internal Task<PersonalTodoItem> CompleteAsync(
        Guid itemId, Guid eventId, long expectedRevision, string? summary,
        CancellationToken cancellationToken) =>
        InvokeAsync<CompletePersonalTodoItemRequest, PersonalTodoItem>(
            PersonalTodoCapabilities.Complete,
            new(itemId, eventId, expectedRevision, summary,
                $"personal-todo-complete:{eventId:N}:{itemId:N}"), cancellationToken);

    internal Task<PersonalTodoItem> BlockAsync(
        Guid itemId, Guid eventId, long expectedRevision, string reason,
        CancellationToken cancellationToken) =>
        InvokeAsync<BlockPersonalTodoItemRequest, PersonalTodoItem>(
            PersonalTodoCapabilities.Block,
            new(itemId, eventId, expectedRevision, reason,
                $"personal-todo-block:{eventId:N}:{itemId:N}"), cancellationToken);

    internal Task<PersonalTodoItem> ReleaseAsync(
        Guid itemId, Guid eventId, long expectedRevision, CancellationToken cancellationToken) =>
        InvokeAsync<ReleasePersonalTodoItemRequest, PersonalTodoItem>(
            PersonalTodoCapabilities.Release,
            new(itemId, eventId, expectedRevision,
                $"personal-todo-release:{eventId:N}:{itemId:N}:{expectedRevision}"),
            cancellationToken);

    private async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string capability, TRequest payload, CancellationToken cancellationToken)
    {
        var result = await _tools.InvokeAsync(capability,
            JsonSerializer.SerializeToElement(payload, JsonOptions), cancellationToken);
        return result.Deserialize<TResponse>(JsonOptions)
            ?? throw new PlatformCapabilityException(capability,
                PlatformCapabilityErrorCode.ValidationFailed,
                "The personal to-do capability returned an empty response.");
    }
}
