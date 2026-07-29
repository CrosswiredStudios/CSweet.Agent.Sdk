using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CSweet.Agent.Contracts.Packaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CSweet.Agent.SDK;

internal sealed class McpAgentRuntimeClient(
    HttpClient http,
    IOptions<AgentRuntimeOptions> options,
    ILogger<McpAgentRuntimeClient> logger) : IAgentRuntimeTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AgentRuntimeOptions _options = options.Value;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private string? _accessToken;
    private long _requestId;

    public AgentRuntimeSession? Session { get; private set; }

    public async Task<AgentRuntimeSession> InitializeAsync(
        AgentManifest manifest,
        CancellationToken cancellationToken)
    {
        var workloadToken = await ReadWorkloadTokenAsync(cancellationToken);
        using var request = CreateRequest(
            "initialize",
            new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { },
                clientInfo = new { name = manifest.Id, version = manifest.Version },
                _meta = new
                {
                    csweet = new
                    {
                        agentId = manifest.Id,
                        agentVersion = manifest.Version,
                        installationId = _options.InstallationId,
                        businessId = _options.BusinessId,
                        runtimeInstanceId = _options.RuntimeInstanceId,
                        tickId = _options.TickId
                    }
                }
            },
            workloadToken);

        using var response = await http.SendAsync(request, cancellationToken);
        var result = await ReadResultAsync(response, cancellationToken);
        var meta = result.GetProperty("_meta").GetProperty("csweet");
        _accessToken = RequiredString(meta, "accessToken");
        var expiresAt = meta.GetProperty("expiresAt").GetDateTimeOffset();
        var sessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var values)
            ? values.Single()
            : RequiredString(meta, "sessionId");
        var identity = meta.TryGetProperty("identity", out var identityElement) &&
                       identityElement.ValueKind == JsonValueKind.Object
            ? identityElement.Deserialize<AgentIdentity>(JsonOptions)
            : null;
        Session = new AgentRuntimeSession(
            sessionId,
            expiresAt,
            meta.GetProperty("grantRevision").GetInt64(),
            identity);
        return Session;
    }

    public async Task<AgentWorkLease?> ClaimAsync(
        int maximumItems,
        CancellationToken cancellationToken)
    {
        var result = await InvokeMethodAsync(
            "csweet/work/claim",
            new
            {
                maximumItems = Math.Max(1, maximumItems),
                waitSeconds = Math.Clamp(_options.ClaimLongPollSeconds, 1, 25)
            },
            cancellationToken);
        if (!result.TryGetProperty("work", out var work) || work.ValueKind == JsonValueKind.Null)
            return null;
        return ParseWork(work);
    }

    public Task RenewWorkAsync(
        AgentWorkLease lease,
        CancellationToken cancellationToken) =>
        InvokeWithoutResultAsync(
            "csweet/work/renew",
            new { workId = lease.WorkId, leaseToken = lease.LeaseToken, attempt = lease.Attempt },
            cancellationToken);

    public Task ReportProgressAsync(
        AgentWorkLease lease,
        long sequence,
        JsonElement value,
        CancellationToken cancellationToken) =>
        InvokeWithoutResultAsync(
            "csweet/work/progress",
            new
            {
                workId = lease.WorkId,
                leaseToken = lease.LeaseToken,
                attempt = lease.Attempt,
                sequence,
                value
            },
            cancellationToken);

    public Task CompleteAsync(
        AgentWorkLease lease,
        AgentWorkResult result,
        CancellationToken cancellationToken) =>
        InvokeWithoutResultAsync(
            "csweet/work/complete",
            new
            {
                workId = lease.WorkId,
                leaseToken = lease.LeaseToken,
                attempt = lease.Attempt,
                result
            },
            cancellationToken);

    public Task FailAsync(
        AgentWorkLease lease,
        string error,
        CancellationToken cancellationToken) =>
        InvokeWithoutResultAsync(
            "csweet/work/fail",
            new
            {
                workId = lease.WorkId,
                leaseToken = lease.LeaseToken,
                attempt = lease.Attempt,
                error
            },
            cancellationToken);

    public Task CompleteRuntimeAsync(
        AgentWorkResult result,
        CancellationToken cancellationToken) =>
        InvokeWithoutResultAsync("csweet/runtime/complete", new { result }, cancellationToken);

    public async Task<JsonElement> InvokeAsync(
        string capability,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var tools = await ListToolsAsync(cancellationToken);
        var descriptor = tools.SingleOrDefault(x =>
            string.Equals(x.Capability, capability, StringComparison.Ordinal))
            ?? throw new PlatformCapabilityException(
                capability,
                PlatformCapabilityErrorCode.Denied,
                $"Capability '{capability}' is not in the active installation grant.");
        var result = await InvokeMethodAsync(
            "tools/call",
            new { name = descriptor.Name, arguments },
            cancellationToken);
        if (result.TryGetProperty("isError", out var isError) && isError.GetBoolean())
            throw new PlatformCapabilityException(
                capability,
                PlatformCapabilityErrorCode.Unavailable,
                ReadToolText(result) ?? $"Capability '{capability}' failed.");
        if (result.TryGetProperty("structuredContent", out var structured) &&
            structured.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            return structured.Clone();
        var text = ReadToolText(result);
        return string.IsNullOrWhiteSpace(text)
            ? JsonSerializer.SerializeToElement(new { }, JsonOptions)
            : JsonDocument.Parse(text).RootElement.Clone();
    }

    public async IAsyncEnumerable<JsonElement> InvokeStreamingAsync(
        string capability,
        JsonElement arguments,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return await InvokeAsync(capability, arguments, cancellationToken);
    }

    public async Task<IReadOnlyList<AgentToolDescriptor>> ListToolsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await InvokeMethodAsync("tools/list", new { }, cancellationToken);
        var revision = result.TryGetProperty("_meta", out var meta) &&
                       meta.TryGetProperty("csweet", out var csweet) &&
                       csweet.TryGetProperty("grantRevision", out var revisionElement)
            ? revisionElement.GetInt64()
            : Session?.GrantRevision ?? 0;
        return result.GetProperty("tools").EnumerateArray().Select(tool =>
        {
            var toolMeta = tool.TryGetProperty("_meta", out var descriptorMeta) &&
                           descriptorMeta.TryGetProperty("csweet", out var descriptorCsweet)
                ? descriptorCsweet
                : default;
            return new AgentToolDescriptor(
                toolMeta.ValueKind == JsonValueKind.Object &&
                toolMeta.TryGetProperty("capability", out var capability)
                    ? capability.GetString() ?? tool.GetProperty("name").GetString()!
                    : tool.GetProperty("name").GetString()!,
                tool.GetProperty("name").GetString()!,
                tool.TryGetProperty("description", out var description) ? description.GetString() ?? string.Empty : string.Empty,
                tool.GetProperty("inputSchema").Clone(),
                tool.TryGetProperty("outputSchema", out var output) ? output.Clone() : null,
                revision,
                toolMeta.ValueKind != JsonValueKind.Object ||
                !toolMeta.TryGetProperty("modelVisible", out var modelVisible) ||
                modelVisible.GetBoolean());
        }).ToList();
    }

    private async Task<JsonElement> InvokeMethodAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        await RenewSessionIfNeededAsync(cancellationToken);
        using var request = CreateRequest(
            method,
            parameters,
            _accessToken ?? throw new InvalidOperationException("The MCP runtime session is not initialized."));
        using var response = await http.SendAsync(request, cancellationToken);
        return await ReadResultAsync(response, cancellationToken);
    }

    private async Task InvokeWithoutResultAsync(
        string method,
        object parameters,
        CancellationToken cancellationToken) =>
        _ = await InvokeMethodAsync(method, parameters, cancellationToken);

    private async Task RenewSessionIfNeededAsync(CancellationToken cancellationToken)
    {
        if (Session is null || Session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            return;
        await _sessionGate.WaitAsync(cancellationToken);
        try
        {
            if (Session.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
                return;
            using var request = CreateRequest(
                "csweet/session/renew",
                new { sessionId = Session.SessionId },
                _accessToken!);
            using var response = await http.SendAsync(request, cancellationToken);
            var result = await ReadResultAsync(response, cancellationToken);
            var meta = result.GetProperty("_meta").GetProperty("csweet");
            _accessToken = RequiredString(meta, "accessToken");
            Session = Session with
            {
                ExpiresAt = meta.GetProperty("expiresAt").GetDateTimeOffset(),
                GrantRevision = meta.GetProperty("grantRevision").GetInt64()
            };
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private HttpRequestMessage CreateRequest(string method, object parameters, string token)
    {
        var payload = new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _requestId).ToString(),
            method,
            @params = parameters
        };
        var request = new HttpRequestMessage(HttpMethod.Post, _options.McpEndpoint)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-06-18");
        if (Session is not null)
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", Session.SessionId);
        request.Headers.TryAddWithoutValidation("X-CSweet-Runtime-Instance-Id", _options.RuntimeInstanceId);
        request.Headers.TryAddWithoutValidation("X-CSweet-Tick-Id", _options.TickId);
        return request;
    }

    private async Task<string> ReadWorkloadTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.WorkloadTokenFile))
            throw new InvalidOperationException("CSweet:Agent:WorkloadTokenFile is required.");
        var token = (await File.ReadAllTextAsync(_options.WorkloadTokenFile, cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("The C-Sweet workload token file is empty.");
        return token;
    }

    private static async Task<JsonElement> ReadResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The MCP service returned an empty response.");
        using (document)
        {
            var root = document.RootElement;
            var hasError = root.TryGetProperty("error", out var error);
            if (!response.IsSuccessStatusCode || hasError)
            {
                var message = error.ValueKind == JsonValueKind.Object &&
                              error.TryGetProperty("message", out var errorMessage)
                    ? errorMessage.GetString()
                    : response.ReasonPhrase;
                throw new InvalidOperationException($"The MCP service rejected the request: {message}");
            }
            return root.GetProperty("result").Clone();
        }
    }

    private static AgentWorkLease ParseWork(JsonElement work) => new(
        work.GetProperty("workId").GetGuid(),
        work.GetProperty("attempt").GetInt32(),
        Enum.Parse<AgentWorkKind>(RequiredString(work, "kind"), ignoreCase: true),
        RequiredString(work, "name"),
        work.GetProperty("payload").Clone(),
        RequiredString(work, "leaseToken"),
        work.GetProperty("leaseExpiresAt").GetDateTimeOffset(),
        work.GetProperty("deadline").GetDateTimeOffset(),
        work.TryGetProperty("eventId", out var eventId) &&
        eventId.ValueKind is not JsonValueKind.Null
            ? eventId.GetGuid()
            : null,
        work.TryGetProperty("correlationId", out var correlationId) ? correlationId.GetString() : null);

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString()
        ?? throw new InvalidOperationException($"The MCP response omitted '{name}'.");

    private static string? ReadToolText(JsonElement result)
    {
        if (!result.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return null;
        return content.EnumerateArray()
            .FirstOrDefault(x => x.TryGetProperty("type", out var type) && type.GetString() == "text")
            .TryGetProperty("text", out var text)
            ? text.GetString()
            : null;
    }

    public ValueTask DisposeAsync()
    {
        _accessToken = null;
        Session = null;
        _sessionGate.Dispose();
        logger.LogDebug("Disposed the C-Sweet MCP runtime client.");
        return ValueTask.CompletedTask;
    }
}
