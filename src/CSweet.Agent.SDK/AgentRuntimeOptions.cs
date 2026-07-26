namespace CSweet.Agent.SDK;

public sealed class AgentRuntimeOptions
{
    public const string SectionName = "CSweet:Agent";

    public string McpEndpoint { get; set; } = "http://agenthost:8081/mcp";

    public string InstallationId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string ManifestPath { get; set; } = "csweet-plugin.json";

    public string RuntimeInstanceId { get; set; } = string.Empty;
    public string TickId { get; set; } = string.Empty;
    public string WorkloadTokenFile { get; set; } = "/run/secrets/csweet-workload-token";
    public int ClaimLongPollSeconds { get; set; } = 25;
    public int LeaseRenewalSeconds { get; set; } = 20;
}
