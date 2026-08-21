using System.Text.Json;

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

    /// <summary>Gets progress values reported during callbacks in their original order.</summary>
    public IReadOnlyList<JsonElement> Progress => _progress;

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
        var invoker = new TestInvoker(_capabilities);
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
        CancellationToken cancellationToken = default)
    {
        var stableEventId = eventId ?? Guid.NewGuid();
        if (stableEventId == Guid.Empty)
            throw new ArgumentException("Event ID must not be empty.", nameof(eventId));
        var occurredAt = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.SerializeToElement(data, JsonOptions);
        var context = CreateContext();
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

    private sealed class TestInvoker(IReadOnlyDictionary<string, TestCapability> capabilities)
        : IPlatformToolInvoker
    {
        public Task<JsonElement> InvokeAsync(
            string capability,
            JsonElement arguments,
            CancellationToken cancellationToken = default) =>
            capabilities.TryGetValue(capability, out var registered)
                ? registered.Handler(arguments, cancellationToken)
                : throw new PlatformCapabilityException(
                    capability,
                    PlatformCapabilityErrorCode.Denied,
                    $"Capability '{capability}' is not registered in this test runtime.");

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
