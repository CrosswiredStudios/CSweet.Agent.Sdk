using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>
/// In-memory SDK runtime for agent and sample tests. It has no network, credentials, MCP,
/// sessions, or lease state.
/// </summary>
public sealed class AgentTestRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, TestCapability> _capabilities = new(StringComparer.Ordinal);
    private readonly List<JsonElement> _progress = [];
    private readonly object _collaborationGate = new();
    private readonly List<(Guid SessionId, string Capability)> _startedCollaborations = [];
    private readonly HashSet<Guid> _trackedCollaborations = [];

    /// <summary>Gets progress values reported during callbacks in their original order.</summary>
    public IReadOnlyList<JsonElement> Progress => _progress;

    /// <summary>
    /// Gets the chat-started and board-started collaborations that no personal card owns yet. A card owns a
    /// collaboration when it was added with that session in its work context. Work-item collaborations are
    /// owned by their ticket and are never listed.
    /// </summary>
    public IReadOnlyList<Guid> UntrackedCollaborations
    {
        get
        {
            lock (_collaborationGate)
                return _startedCollaborations.Where(x => !_trackedCollaborations.Contains(x.SessionId))
                    .Select(x => x.SessionId).Distinct().ToList();
        }
    }

    /// <summary>
    /// Fails the test when the agent started a chat or board collaboration without an owning card. Call it
    /// after the callbacks under test. See <see cref="TrackedCollaboration"/>.
    /// </summary>
    public void AssertCollaborationsTracked()
    {
        List<string> untracked;
        lock (_collaborationGate)
            untracked = _startedCollaborations.Where(x => !_trackedCollaborations.Contains(x.SessionId))
                .Select(x => $"{x.SessionId:D} ({x.Capability})").Distinct(StringComparer.Ordinal).ToList();
        if (untracked.Count > 0)
            throw new InvalidOperationException(
                "The agent started a collaboration with no owning card: " + string.Join(", ", untracked) +
                ". Call TrackedCollaboration.QueueCardAsync after starting it, so the work is visible on a board " +
                "and can be resumed through the card.");
    }

    private void ObserveCollaboration(string capability, JsonElement arguments, JsonElement result)
    {
        if (capability == CommunicationCapabilities.CoordinationStart ||
            capability == CommunicationCapabilities.CoordinationStartBoard)
        {
            if (TryGuid(result, "id", out var sessionId))
                lock (_collaborationGate) _startedCollaborations.Add((sessionId, capability));
        }
        else if (capability == PersonalTodoCapabilities.Add &&
                 TryProperty(arguments, "workContext", out var workContext) &&
                 TryGuid(workContext, "coordinationSessionId", out var trackedId))
        {
            lock (_collaborationGate) _trackedCollaborations.Add(trackedId);
        }
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        return false;
    }

    private static bool TryGuid(JsonElement element, string name, out Guid value)
    {
        value = Guid.Empty;
        return TryProperty(element, name, out var property) && property.ValueKind == JsonValueKind.String &&
               Guid.TryParse(property.GetString(), out value) && value != Guid.Empty;
    }

    /// <summary>Registers one fake granted capability for an in-memory agent test.</summary>
    public AgentTestRuntime RegisterCapability<TRequest, TResponse>(
        string capability,
        Func<TRequest, CancellationToken, Task<TResponse>> handler,
        string? toolName = null,
        string? description = null,
        bool modelVisible = false)
    {
        _capabilities[capability] = new TestCapability(
            toolName ?? ToToolName(capability),
            description ?? $"In-memory test capability {capability}.",
            Schema("""{"type":"object","additionalProperties":true}"""),
            null,
            modelVisible,
            async (arguments, token) =>
            {
                AgentOperatingStateValidation.ValidateInput(capability, arguments);
                var request = arguments.Deserialize<TRequest>(JsonOptions)
                    ?? throw new InvalidOperationException($"Test capability '{capability}' received an empty request.");
                return JsonSerializer.SerializeToElement(await handler(request, token), JsonOptions);
            });
        return this;
    }

    /// <summary>Creates a callback context with optional server-resolved test identities.</summary>
    public AgentRuntimeContext CreateContext(
        string businessId = "test-organization",
        string installationId = "00000000-0000-0000-0000-000000000001",
        AgentIdentity? identity = null)
    {
        var invoker = new TestInvoker(_capabilities, ObserveCollaboration);
        return new AgentRuntimeContext(
            businessId,
            installationId,
            "00000000-0000-0000-0000-000000000002",
            "00000000-0000-0000-0000-000000000003",
            new PlatformCapabilityClient(invoker),
            new TestProgressReporter(_progress),
            identity);
    }

    /// <summary>Executes one capability callback without network or runtime credentials.</summary>
    public Task<AgentWorkResult> ExecuteCapabilityAsync(
        ICSweetAgent agent,
        string capability,
        object arguments,
        CancellationToken cancellationToken = default) =>
        agent.ExecuteCapabilityAsync(
            new AgentCapabilityRequest(
                Guid.NewGuid(),
                capability,
                JsonSerializer.SerializeToElement(arguments, JsonOptions),
                "test-correlation"),
            CreateContext(),
            cancellationToken);

    /// <summary>Delivers one event callback without network or runtime credentials.</summary>
    public async Task DeliverEventAsync(
        ICSweetAgent agent,
        string eventType,
        object data,
        Guid? eventId = null,
        CancellationToken cancellationToken = default,
        AgentIdentity? identity = null)
    {
        var stableEventId = eventId ?? Guid.NewGuid();
        if (stableEventId == Guid.Empty)
            throw new ArgumentException("Event ID must not be empty.", nameof(eventId));
        var occurredAt = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.SerializeToElement(data, JsonOptions);
        var context = CreateContext(identity: identity);
        var envelope = new AgentEventEnvelope(Guid.NewGuid(), stableEventId, eventType, payload, occurredAt, "test-correlation");
        if (agent is CSweetManagerAgentBase managerAgent && await ManagerIncidentCoordinator.DispatchAsync(managerAgent, envelope, context, cancellationToken)) return;
        if (agent is CSweetAgentBase delegationAgent && await DelegationRuntime.DispatchAsync(delegationAgent, envelope, context, cancellationToken)) return;
        if (string.Equals(eventType, AgentAttentionEvents.ReviewDue, StringComparison.Ordinal) &&
            agent is CSweetAgentBase attentiveAgent)
        {
            var review = payload.Deserialize<AgentAttentionReviewDueEvent>(JsonOptions)
                ?? throw new InvalidOperationException("The attention review payload is empty.");
            await attentiveAgent.HandleAttentionReviewAsync(
                new AgentAttentionReviewContext(
                    review.ReviewId, review.OccurredAt, review.NextReviewAt, review.Reason),
                context, cancellationToken);
            return;
        }
        await agent.HandleEventAsync(
            new AgentEventEnvelope(
                Guid.NewGuid(), stableEventId, eventType, payload,
                occurredAt, "test-correlation"),
            context, cancellationToken);
    }

    private sealed record TestCapability(
        string Name,
        string Description,
        JsonElement InputSchema,
        JsonElement? OutputSchema,
        bool ModelVisible,
        Func<JsonElement, CancellationToken, Task<JsonElement>> Handler);

    private sealed class TestInvoker(
        IReadOnlyDictionary<string, TestCapability> capabilities,
        Action<string, JsonElement, JsonElement> observe)
        : IPlatformToolInvoker
    {
        public async Task<JsonElement> InvokeAsync(
            string capability,
            JsonElement arguments,
            CancellationToken cancellationToken = default)
        {
            if (!capabilities.TryGetValue(capability, out var registered))
                throw new PlatformCapabilityException(
                    capability,
                    PlatformCapabilityErrorCode.Denied,
                    $"Capability '{capability}' is not registered in this test runtime.");
            var result = await registered.Handler(arguments, cancellationToken);
            observe(capability, arguments, result);
            return result;
        }

        public Task<IReadOnlyList<AgentToolDescriptor>> ListToolsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentToolDescriptor>>(capabilities.Select(pair =>
                new AgentToolDescriptor(
                    pair.Key,
                    pair.Value.Name,
                    pair.Value.Description,
                    pair.Value.InputSchema,
                    pair.Value.OutputSchema,
                    1,
                    pair.Value.ModelVisible)).ToList());

        public async IAsyncEnumerable<JsonElement> InvokeStreamingAsync(
            string capability,
            JsonElement arguments,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return await InvokeAsync(capability, arguments, cancellationToken);
        }
    }

    private sealed class TestProgressReporter(List<JsonElement> progress) : IAgentProgressReporter
    {
        public Task ReportAsync(object? value, CancellationToken cancellationToken = default)
        {
            progress.Add(JsonSerializer.SerializeToElement(value, JsonOptions));
            return Task.CompletedTask;
        }
    }

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string ToToolName(string capability) =>
        string.Concat(capability.Select(x => char.IsLetterOrDigit(x) ? char.ToLowerInvariant(x) : '_'))
            .Trim('_');
}
