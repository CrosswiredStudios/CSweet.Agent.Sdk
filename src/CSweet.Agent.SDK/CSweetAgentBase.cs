using System.Text.Json;

namespace CSweet.Agent.SDK;

public abstract class CSweetAgentBase : ICSweetAgent
{
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly object _settingsLock = new();
    private AgentConfigurationDefinition? _configuration;
    private Dictionary<string, JsonElement>? _settings;

    public abstract string AgentId { get; }

    public abstract string Version { get; }

    protected virtual string ConfigurationSchemaVersion => "1.0";

    protected AgentSettings Settings
    {
        get
        {
            lock (_settingsLock)
            {
                EnsureConfiguration();
                return new AgentSettings(CloneSettings(_settings!));
            }
        }
    }

    public virtual Task HandleEventAsync(
        AgentEventEnvelope message,
        AgentRuntimeContext context,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task<AgentWorkResult> ExecuteCapabilityAsync(
        AgentCapabilityRequest request,
        AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        if (request.Capability == AgentConfigurationCapabilities.Describe)
        {
            return AgentWorkResult.Success(CreateConfigurationSchema());
        }

        if (request.Capability == AgentConfigurationCapabilities.Update)
        {
            return UpdateConfiguration(request);
        }

        return await ExecuteCapabilityCoreAsync(request, context, cancellationToken);
    }

    protected virtual Task<AgentWorkResult> ExecuteCapabilityCoreAsync(
        AgentCapabilityRequest request,
        AgentRuntimeContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult(AgentWorkResult.Failure(
            $"Capability '{request.Capability}' is not supported by this agent."));

    protected virtual AgentConfigurationBuilder Configure(AgentConfigurationBuilder builder) => builder;

    protected virtual string? ValidateConfigurationUpdate(
        AgentConfigurationField field,
        JsonElement value,
        AgentSettings currentSettings) =>
        null;

    protected static T? DeserializePayload<T>(JsonElement payload) =>
        payload.Deserialize<T>(SerializerOptions);

    protected static JsonElement SerializePayload<T>(T payload) =>
        JsonSerializer.SerializeToElement(payload, SerializerOptions);

    private AgentConfigurationSchemaResponse CreateConfigurationSchema()
    {
        lock (_settingsLock)
        {
            var configuration = EnsureConfiguration();
            return new AgentConfigurationSchemaResponse(
                AgentId,
                Version,
                ConfigurationSchemaVersion,
                configuration.Fields,
                CloneSettings(_settings!));
        }
    }

    private AgentWorkResult UpdateConfiguration(AgentCapabilityRequest request)
    {
        UpdateAgentConfigurationRequest? update;
        try
        {
            update = DeserializePayload<UpdateAgentConfigurationRequest>(request.Arguments);
        }
        catch (JsonException)
        {
            return AgentWorkResult.Failure("The configuration payload is not valid JSON.");
        }

        if (update is null)
        {
            return AgentWorkResult.Failure("The configuration payload is required.");
        }

        lock (_settingsLock)
        {
            var configuration = EnsureConfiguration();
            var validationError = ValidateSettings(configuration, update.Settings);
            if (validationError is not null)
            {
                return AgentWorkResult.Failure(validationError);
            }

            _settings = MergeSettings(_settings!, update.Settings);
            var response = new AgentConfigurationUpdateResponse(
                true,
                "Agent settings updated.",
                CloneSettings(_settings));

            return new AgentWorkResult(true, SerializePayload(response));
        }
    }

    private string? ValidateSettings(
        AgentConfigurationDefinition configuration,
        IReadOnlyDictionary<string, JsonElement> settings)
    {
        var knownFields = configuration.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        var currentSettings = new AgentSettings(CloneSettings(_settings!));

        foreach (var (key, value) in settings)
        {
            if (!knownFields.TryGetValue(key, out var field))
            {
                return $"Setting '{key}' is not supported by this agent.";
            }

            var validationError = ValidateField(field, value) ??
                ValidateConfigurationUpdate(field, value, currentSettings);

            if (validationError is not null)
            {
                return validationError;
            }
        }

        return null;
    }

    private static string? ValidateField(AgentConfigurationField field, JsonElement value)
    {
        if (field.Required && IsEmpty(value))
        {
            return $"Setting '{field.Key}' is required.";
        }

        switch (field.Type)
        {
            case AgentConfigurationFieldTypes.Select:
                if (value.ValueKind != JsonValueKind.String ||
                    field.Options is null ||
                    !field.Options.Any(option => option.Value == value.GetString()))
                {
                    return $"Setting '{field.Key}' must be one of the values exposed by this agent.";
                }
                break;

            case AgentConfigurationFieldTypes.Boolean:
                if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    return $"Setting '{field.Key}' must be true or false.";
                }
                break;

            case AgentConfigurationFieldTypes.Number:
                if (value.ValueKind != JsonValueKind.Number ||
                    !value.TryGetDecimal(out var number) ||
                    (field.Minimum is not null && number < field.Minimum) ||
                    (field.Maximum is not null && number > field.Maximum))
                {
                    return $"Setting '{field.Key}' is outside the supported range.";
                }
                break;

            case AgentConfigurationFieldTypes.Text:
            case AgentConfigurationFieldTypes.TextArea:
            case AgentConfigurationFieldTypes.Secret:
            case AgentConfigurationFieldTypes.LlmProvider:
            case AgentConfigurationFieldTypes.LlmModel:
                if (value.ValueKind is not JsonValueKind.String and not JsonValueKind.Null)
                {
                    return $"Setting '{field.Key}' must be text.";
                }

                if (field.Type == AgentConfigurationFieldTypes.LlmProvider &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()) &&
                    !Guid.TryParse(value.GetString(), out _))
                {
                    return $"Setting '{field.Key}' must be a provider profile id.";
                }
                break;
        }

        return null;
    }

    private AgentConfigurationDefinition EnsureConfiguration()
    {
        if (_configuration is not null)
        {
            return _configuration;
        }

        _configuration = Configure(new AgentConfigurationBuilder()).Build();
        _settings = CloneSettings(_configuration.DefaultSettings);
        return _configuration;
    }

    private static bool IsEmpty(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ||
        (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));

    private static Dictionary<string, JsonElement> MergeSettings(
        IReadOnlyDictionary<string, JsonElement> current,
        IReadOnlyDictionary<string, JsonElement> updates)
    {
        var merged = CloneSettings(current);
        foreach (var (key, value) in updates)
        {
            merged[key] = value.Clone();
        }

        return merged;
    }

    private static Dictionary<string, JsonElement> CloneSettings(IReadOnlyDictionary<string, JsonElement> settings) =>
        settings.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
}
