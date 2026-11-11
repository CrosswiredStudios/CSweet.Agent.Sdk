using System.Net.Http.Headers;

namespace CSweet.Agent.SDK;

internal sealed partial class McpAgentRuntimeClient
{
    private readonly object _gitGate = new();
    private AgentGitForwarder? _gitForwarder;

    /// <summary>Starts the loopback forwarder on first use and returns what git needs to reach it.</summary>
    public AgentGitRemoteAccess? OpenGitRemote(Guid workspaceId, long assignmentRevision)
    {
        lock (_gitGate)
        {
            if (_gitForwarder is null)
            {
                var forwarder = new AgentGitForwarder(this, _logger);
                forwarder.Start();
                _gitForwarder = forwarder;
            }
            var access = new AgentGitRemoteAccess(
                _gitForwarder.RemoteUrl(workspaceId, assignmentRevision),
                _gitForwarder.GitEnvironment());
            if (Session?.Identity is not { } identity) return access;
            // Angle brackets and line breaks would break the commit header; drop them.
            var name = new string(identity.DisplayName.Where(c => c is not ('<' or '>') && !char.IsControl(c)).ToArray()).Trim();
            var employee = new string(identity.EmployeeId.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
            return access with
            {
                AuthorName = name.Length is > 0 and <= 200 ? name : access.AuthorName,
                AuthorEmail = employee.Length is > 0 and <= 100 ? $"{employee}@agents.csweet.invalid" : access.AuthorEmail
            };
        }
    }

    /// <summary>
    /// Sends one Git exchange to the platform under the current runtime session. The platform
    /// decides whether this runtime may read or push the workspace named in the path.
    /// </summary>
    public async Task<AgentGitExchangeResult> ExchangeGitAsync(
        AgentGitExchange exchange,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        if (!AgentGitRoutes.IsSupported(exchange.Method, exchange.PathAndQuery))
            throw new ArgumentException("The Git route is not supported.", nameof(exchange));
        if (exchange.Method == "GET" && !exchange.Body.IsEmpty)
            throw new ArgumentException("A Git ref advertisement carries no body.", nameof(exchange));
        if (exchange.Body.Length > AgentGitRoutes.MaximumBodyBytes)
            throw new ArgumentException("The Git request exceeds the broker transfer limit.", nameof(exchange));
        var session = Session ?? throw new InvalidOperationException("The MCP runtime session is not initialized.");
        await RenewSessionIfNeededAsync(cancellationToken);
        var token = _accessToken ?? throw new InvalidOperationException("The MCP runtime session is not initialized.");

        using var request = new HttpRequestMessage(
            new HttpMethod(exchange.Method),
            new Uri(new Uri(_options.McpEndpoint), exchange.PathAndQuery));
        if (exchange.Method == "POST")
            request.Content = new ReadOnlyMemoryContent(exchange.Body);
        foreach (var header in exchange.Headers)
        {
            if (!AgentGitRoutes.ForwardedRequestHeaders.Contains(header.Key)) continue;
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session.SessionId);
        request.Headers.TryAddWithoutValidation("X-CSweet-Runtime-Instance-Id", _options.RuntimeInstanceId);
        request.Headers.TryAddWithoutValidation("X-CSweet-Tick-Id", _options.TickId);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_capabilityRequestTimeout);
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var body = await ReadGitBodyAsync(response.Content, timeout.Token);
            return new AgentGitExchangeResult(
                (int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The Git exchange did not complete within the capability request timeout.");
        }
    }

    private static async Task<byte[]> ReadGitBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var body = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (body.Length + read > AgentGitRoutes.MaximumBodyBytes)
                throw new InvalidDataException("The Git response exceeds the broker transfer limit.");
            body.Write(buffer, 0, read);
        }
        return body.ToArray();
    }
}
