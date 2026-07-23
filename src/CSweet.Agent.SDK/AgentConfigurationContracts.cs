using System.Text.Json;

namespace CSweet.Agent.SDK;

public sealed record AgentConfigurationSchemaResponse(
    string AgentId,
    string AgentVersion,
    string SchemaVersion,
    IReadOnlyList<AgentConfigurationField> Fields,
    IReadOnlyDictionary<string, JsonElement> Settings);

public sealed record AgentConfigurationField(
    string Key,
    string Label,
    string Type,
    bool Required,
    string? Description = null,
    string? Placeholder = null,
    IReadOnlyList<AgentConfigurationOption>? Options = null,
    decimal? Minimum = null,
    decimal? Maximum = null,
    decimal? Step = null,
    string? DependsOnFieldKey = null);

public sealed record AgentConfigurationOption(string Value, string Label);

public sealed record UpdateAgentConfigurationRequest(
    IReadOnlyDictionary<string, JsonElement> Settings);

public sealed record AgentConfigurationUpdateResponse(
    bool Succeeded,
    string? Message,
    IReadOnlyDictionary<string, JsonElement> Settings);

public static class AgentConfigurationCapabilities
{
    public const string Describe = CapabilityNames.Agent.ConfigurationDescribe;
    public const string Update = CapabilityNames.Agent.ConfigurationUpdate;
}

public static class AgentConfigurationFieldTypes
{
    public const string Text = "text";
    public const string TextArea = "textarea";
    public const string Number = "number";
    public const string Boolean = "boolean";
    public const string Select = "select";
    public const string Secret = "secret";
    public const string LlmProvider = "llmProvider";
    public const string LlmModel = "llmModel";
}
