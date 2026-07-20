using System.Net.Http.Headers;
using CSweet.Agent.Contracts.Grpc;

namespace CSweet.Agent.SDK;

/// <summary>Language-neutral MCP connection metadata issued by broker registration.</summary>
public sealed record McpConnectionInfo(
    Uri Endpoint,
    string AccessToken,
    DateTimeOffset ExpiresAt,
    long GrantRevision,
    IReadOnlyList<string> GrantedRequestedCapabilities,
    IReadOnlyList<string> GlobalCapabilities)
{
    public static McpConnectionInfo FromRegistration(RegistrationResult registration)
    {
        if (!registration.Accepted || string.IsNullOrWhiteSpace(registration.McpEndpoint) ||
            string.IsNullOrWhiteSpace(registration.McpAccessToken) || registration.McpTokenExpiresAt is null)
            throw new InvalidOperationException("Broker registration did not include a valid MCP session.");
        return new(new Uri(registration.McpEndpoint, UriKind.Absolute), registration.McpAccessToken,
            registration.McpTokenExpiresAt.ToDateTimeOffset(), registration.GrantRevision,
            registration.GrantedRequestedCapabilities.ToList(),
            registration.GlobalCapabilities.ToList());
    }

    public HttpClient CreateHttpClient(HttpMessageHandler? handler = null)
    {
        if (ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidOperationException("The MCP access token has expired.");
        var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        client.BaseAddress = Endpoint;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        return client;
    }
}
