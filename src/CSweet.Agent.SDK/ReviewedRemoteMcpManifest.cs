using CSweet.Agent.Contracts.Packaging;
using System.Text.Json;
namespace CSweet.Agent.Contracts.Packaging
{
    /// <summary>Public declarations only. Keys and account-selection arguments are never part of the authoring contract.</summary>
    public sealed record ReviewedRemoteMcpManifest
    {
        public string Profile { get; init; } = "";
        public string Endpoint { get; init; } = "";
        public string Authentication { get; init; } = "";
        public string Header { get; init; } = "";
        public string AccountBinding { get; init; } = "";
        public string Transport { get; init; } = "";
        public IReadOnlyList<string> ProtocolVersions { get; init; } = [];
    }
}
namespace CSweet.Agent.SDK
{
    /// <summary>Distinct closed mode; the OAuth connector validator remains unchanged.</summary>
    public static class ReviewedRemoteMcpValidator
    {
        private static readonly JsonElement Schemas = JsonDocument.Parse("""
{"realtyleads.inheritance.search.v1":{"inputSchema":{"type":"object","additionalProperties":false,"properties":{"state":{"type":["string","null"],"maxLength":128},"county":{"type":["string","null"],"maxLength":128},"city":{"type":["string","null"],"maxLength":128},"status":{"type":["string","null"],"maxLength":128},"minScore":{"type":["number","null"],"minimum":0,"maximum":100},"limit":{"type":"integer","minimum":1,"maximum":100}}},"outputSchema":{"type":"array","maxItems":100,"items":{"type":"object","additionalProperties":false,"properties":{"inheritanceLeadId":{"type":["string","null"]},"propertyId":{"type":["string","null"]},"address":{"type":["string","null"]},"ownerName":{"type":["string","null"]},"status":{"type":["string","null"]},"deceasedId":{"type":["string","null"]},"deceasedName":{"type":["string","null"]},"cityName":{"type":["string","null"]},"countyName":{"type":["string","null"]},"stateAbbreviation":{"type":["string","null"]},"score":{"type":["number","null"]}},"required":["inheritanceLeadId","propertyId","address","ownerName","status","deceasedId","deceasedName","cityName","countyName","stateAbbreviation","score"]}}},"realtyleads.inheritance.lookup.v1":{"inputSchema":{"type":"object","additionalProperties":false,"required":["id"],"properties":{"id":{"type":"string","format":"uuid"}}},"outputSchema":{"type":"object","additionalProperties":false,"properties":{"inheritanceLeadId":{"type":["string","null"]},"propertyId":{"type":["string","null"]},"address":{"type":["string","null"]},"ownerName":{"type":["string","null"]},"status":{"type":["string","null"]},"deceasedId":{"type":["string","null"]},"deceasedName":{"type":["string","null"]},"cityName":{"type":["string","null"]},"countyName":{"type":["string","null"]},"stateAbbreviation":{"type":["string","null"]},"score":{"type":["number","null"]}},"required":["inheritanceLeadId","propertyId","address","ownerName","status","deceasedId","deceasedName","cityName","countyName","stateAbbreviation","score"]}}}
""").RootElement.Clone();
        public static IReadOnlyList<string> Validate(AgentManifest manifest)
        {
            if (manifest.Protocol is null || manifest.Runtime is null) return ["Protocol and runtime declarations are required."];
            if (manifest.Kind != "remote-mcp")
                return manifest.ReviewedRemoteMcp is null && manifest.Protocol.MinimumVersion != "2.5" ? [] : ["Reviewed remote MCP requires kind remote-mcp and protocol 2.5."];
            var d = manifest.ReviewedRemoteMcp;
            if (manifest.Id != "com.crosswiredstudios.plugin.realtyleads" || manifest.Publisher.Id != "com.crosswiredstudios" ||
                manifest.ManifestVersion != "2.0" || manifest.Protocol.MinimumVersion != "2.5" ||
                manifest.Runtime.Type != "host-remote-mcp" || manifest.Runtime.ProjectPath is not null ||
                manifest.Runtime.WorkspaceAccess != "None" || manifest.Runtime.SupportsMultipleInstallations ||
                manifest.Requires.Count != 0 || manifest.Dependencies.Count != 0 || manifest.Configuration.Count != 0 ||
                manifest.Connections.Count != 0 || manifest.Credentials.Count != 0 || manifest.McpServers.Count != 0 ||
                manifest.ProviderOperations.Count != 0 || manifest.FileTransferTargets.Count != 0 || manifest.Accounts is not null ||
                manifest.Setup is not null || manifest.RolePolicy is not null || manifest.WebAccess.Mode != "None" || manifest.WebAccess.Rules.Count != 0 ||
                manifest.Events.Subscribes.Count != 0 || manifest.Ui.Count != 0 || d is null ||
                d.Profile != "realtyleads.read.v1" || d.Endpoint != "https://realtyleads.ai/mcp" || d.Authentication != "api-key" ||
                d.Header != "X-Api-Key" || d.AccountBinding != "owner-business-installation.v1" || d.Transport != "stateless-post" ||
                !d.ProtocolVersions.SequenceEqual(new[] { "2025-03-26" }))
                return ["The reviewed remote MCP declaration does not match the closed RealtyLeads read-only host profile."];
            if (!manifest.Provides.Select(x => x.Name).Order().SequenceEqual(new[] { "realtyleads.inheritance.lookup.v1", "realtyleads.inheritance.search.v1" }) ||
                manifest.Provides.Any(x => x.Idempotency != "none" || x.ExecutionTimeoutSeconds > 30))
                return ["The remote MCP profile allows only bounded inheritance search and lookup reads."];
            foreach (var capability in manifest.Provides)
            {
                var expected = Schemas.GetProperty(capability.Name);
                if (!JsonElement.DeepEquals(capability.InputSchema, expected.GetProperty("inputSchema")) ||
                    !JsonElement.DeepEquals(capability.OutputSchema, expected.GetProperty("outputSchema")))
                    return ["The capability schemas differ from the reviewed RealtyLeads read profile."];
            }
            return [];
        }
    }
}
