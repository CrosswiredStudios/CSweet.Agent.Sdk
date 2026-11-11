using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CSweet.Agent.SDK;

/// <summary>
/// Lets the git program inside this runtime reach the platform. It listens on the loopback
/// address only, accepts only the fixed Git routes, and relays each exchange through the
/// authenticated runtime session. The session credentials stay in this process: git is given a
/// per-process forwarder secret instead, so a web page or other local client that merely knows
/// the port cannot use the forwarder.
/// </summary>
internal sealed class AgentGitForwarder(IAgentGitTransport transport, ILogger logger) : IAsyncDisposable
{
    internal const string SecretHeader = "X-CSweet-Git-Forwarder";
    private const int BindAttempts = 5;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    private HttpListener? _listener;
    private Task? _acceptLoop;

    internal int Port { get; private set; }

    internal void Start()
    {
        if (_listener is not null) throw new InvalidOperationException("The Git forwarder has already started.");
        Exception? last = null;
        for (var attempt = 0; attempt < BindAttempts; attempt++)
        {
            var port = ReserveLoopbackPort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();
            }
            catch (Exception exception) when (exception is HttpListenerException or SocketException)
            {
                // Another process took the port between the reservation and the bind.
                last = exception;
                listener.Close();
                continue;
            }
            _listener = listener;
            Port = port;
            _acceptLoop = AcceptLoopAsync(listener, _lifetime.Token);
            return;
        }
        throw new IOException("The Git forwarder could not bind a loopback port.", last);
    }

    /// <summary>The remote URL for one assigned workspace. It contains no credential.</summary>
    internal string RemoteUrl(Guid workspaceId, long assignmentRevision)
    {
        if (_listener is null) throw new InvalidOperationException("The Git forwarder has not started.");
        return $"http://127.0.0.1:{Port}{AgentGitRoutes.WorkspacePath(workspaceId, assignmentRevision)}";
    }

    /// <summary>
    /// Environment for a git child process. It supplies the forwarder secret, disables any
    /// configured HTTP proxy for this remote, and stops git from prompting.
    /// </summary>
    internal IReadOnlyDictionary<string, string> GitEnvironment() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GIT_CONFIG_COUNT"] = "2",
        ["GIT_CONFIG_KEY_0"] = "http.extraHeader",
        ["GIT_CONFIG_VALUE_0"] = $"{SecretHeader}: {_secret}",
        ["GIT_CONFIG_KEY_1"] = "http.proxy",
        ["GIT_CONFIG_VALUE_1"] = string.Empty,
        ["GIT_TERMINAL_PROMPT"] = "0"
    };

    private static int ReserveLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            _ = HandleAsync(context, cancellationToken);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var request = context.Request;
            if (!IsAuthorized(request.Headers[SecretHeader]))
            {
                await RespondAsync(context, 403, null, [], cancellationToken);
                return;
            }
            var method = request.HttpMethod;
            var target = request.RawUrl ?? string.Empty;
            if (!AgentGitRoutes.IsSupported(method, target))
            {
                await RespondAsync(context, 404, null, [], cancellationToken);
                return;
            }
            byte[] body = [];
            if (method == "POST")
            {
                var read = await ReadBoundedAsync(request.InputStream, cancellationToken);
                if (read is null)
                {
                    await RespondAsync(context, 413, "text/plain",
                        Encoding.UTF8.GetBytes("The Git request exceeds the broker transfer limit. Large files must use Git LFS."),
                        cancellationToken);
                    return;
                }
                body = read;
            }
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in AgentGitRoutes.ForwardedRequestHeaders)
            {
                var value = request.Headers[name];
                if (!string.IsNullOrEmpty(value)) headers[name] = value;
            }
            var result = await transport.ExchangeGitAsync(new AgentGitExchange(method, target, headers, body), cancellationToken);
            await RespondAsync(context, result.StatusCode, result.ContentType, result.Body, cancellationToken);
        }
        catch (Exception exception)
        {
            // Never log headers or bodies here: the request carries the forwarder secret.
            if (exception is not OperationCanceledException)
                logger.LogWarning("The Git forwarder could not complete an exchange: {Failure}.", exception.GetType().Name);
            try { await RespondAsync(context, 502, null, [], CancellationToken.None); }
            catch (Exception) { context.Response.Abort(); }
        }
    }

    private bool IsAuthorized(string? presented) =>
        presented is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(_secret));

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (body.Length + read > AgentGitRoutes.MaximumBodyBytes) return null;
            body.Write(buffer, 0, read);
        }
        return body.ToArray();
    }

    private static async Task RespondAsync(
        HttpListenerContext context,
        int statusCode,
        string? contentType,
        byte[] body,
        CancellationToken cancellationToken)
    {
        var response = context.Response;
        response.StatusCode = statusCode is >= 100 and <= 599 ? statusCode : 502;
        if (contentType is not null) response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = body.Length;
        if (body.Length > 0) await response.OutputStream.WriteAsync(body, cancellationToken);
        response.Close();
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        _listener?.Close();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException) { }
        }
        _lifetime.Dispose();
    }
}
