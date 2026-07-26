using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CSweet.Agent.SDK;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddCSweetAgent<TAgent>(
        this IHostApplicationBuilder builder)
        where TAgent : class, ICSweetAgent
    {
        var section = builder.Configuration.GetSection(AgentRuntimeOptions.SectionName);

        builder.Services
            .AddOptions<AgentRuntimeOptions>()
            .Bind(section)
            .Validate(
                options => Guid.TryParse(options.InstallationId, out _),
                "CSweet:Agent:InstallationId must be a UUID.")
            .Validate(
                options => Guid.TryParse(options.BusinessId, out _),
                "CSweet:Agent:BusinessId must be a UUID.")
            .Validate(
                options => Uri.TryCreate(options.McpEndpoint, UriKind.Absolute, out var endpoint) &&
                           (endpoint.Scheme == Uri.UriSchemeHttp ||
                            endpoint.Scheme == Uri.UriSchemeHttps),
                "CSweet:Agent:McpEndpoint must be an absolute HTTP(S) URI.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.WorkloadTokenFile),
                "CSweet:Agent:WorkloadTokenFile is required.")
            .Validate(
                options => Guid.TryParse(options.RuntimeInstanceId, out _),
                "CSweet:Agent:RuntimeInstanceId must be a UUID.")
            .Validate(
                options => Guid.TryParse(options.TickId, out _),
                "CSweet:Agent:TickId must be a UUID.")
            .ValidateOnStart();

        builder.Services.AddHttpClient("CSweet.Agent.Runtime", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        builder.Services.AddSingleton<McpAgentRuntimeClient>(services =>
            new McpAgentRuntimeClient(
                services.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("CSweet.Agent.Runtime"),
                services.GetRequiredService<IOptions<AgentRuntimeOptions>>(),
                services.GetRequiredService<ILogger<McpAgentRuntimeClient>>()));
        builder.Services.AddSingleton<IAgentRuntimeTransport>(
            services => services.GetRequiredService<McpAgentRuntimeClient>());
        builder.Services.AddSingleton<AgentPlatformAccessor>();
        builder.Services.AddSingleton<TAgent>();
        builder.Services.AddHostedService<AgentRuntimeWorker<TAgent>>();

        return builder;
    }
}
