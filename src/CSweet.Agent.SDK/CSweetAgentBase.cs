using System.Text.Json;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agent.SDK;

/// <summary>
/// Recommended base class for agent callbacks, typed payload serialization, and declarative
/// installation configuration.
/// </summary>
public abstract class CSweetAgentBase : ICSweetAgent
{
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly object _settingsLock = new();
    private AgentConfigurationDefinition? _configuration;
    private Dictionary<string, JsonElement>? _settings;
    private long _configurationRevision;

    /// <summary>Gets the stable package identity declared by the root manifest.</summary>
    public abstract string AgentId { get; }

    /// <summary>Gets the semantic version declared by the root manifest.</summary>
    public abstract string Version { get; }

    /// <summary>Gets the agent-owned configuration schema version.</summary>
    protected virtual string ConfigurationSchemaVersion => "1.0";

    /// <summary>Gets a snapshot of the current installation settings.</summary>
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

    /// <summary>Handles a subscribed durable event. Unknown events should be ignored safely.</summary>
    public virtual Task HandleEventAsync(
        AgentEventEnvelope message,
        AgentRuntimeContext context,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>
    /// Handles the next atomically claimed item from this installation's personal queue. The SDK
    /// owns claiming and transitions; implementations report Completed, InProgress, or Blocked.
    /// </summary>
    public virtual Task<PersonalTodoResult> HandlePersonalTodoAsync(
        PersonalTodoItem item,
        AgentRuntimeContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult(PersonalTodoResult.Blocked(
            $"Agent '{AgentId}' does not support personal to-do work."));

    /// <summary>Reconciles durable role commitments without requiring a model call.</summary>
    public virtual Task HandleAttentionReviewAsync(
        AgentAttentionReviewContext review,
        AgentRuntimeContext context,
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Handles one durable turn in a platform-governed agent collaboration. Implementations must
    /// explicitly continue, complete, or block; the SDK submits the returned disposition.
    /// </summary>
    public virtual Task<AgentCoordinationTurnResult> HandleCoordinationTurnAsync(
        AgentCoordinationTurnRequest request,
        AgentRuntimeContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult(AgentCoordinationTurnResult.Blocked(
            $"Agent '{AgentId}' does not implement collaborative coordination turns."));

    /// <summary>
    /// Dispatches built-in configuration capabilities before invoking the agent capability hook.
    /// </summary>
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

    /// <summary>Implements capabilities declared by this package.</summary>
    protected virtual Task<AgentWorkResult> ExecuteCapabilityCoreAsync(
        AgentCapabilityRequest request,
        AgentRuntimeContext context,
        CancellationToken cancellationToken) =>
        Task.FromResult(AgentWorkResult.Failure(
            $"Capability '{request.Capability}' is not supported by this agent."));

    /// <summary>Declares installation configuration fields and their defaults.</summary>
    protected virtual AgentConfigurationBuilder Configure(AgentConfigurationBuilder builder) => builder;

    /// <summary>Performs agent-specific validation for a proposed configuration value.</summary>
    protected virtual string? ValidateConfigurationUpdate(
        AgentConfigurationField field,
        JsonElement value,
        AgentSettings currentSettings) =>
        null;

    /// <summary>
    /// Observes an atomically installed control-plane configuration snapshot. Return
    /// <see cref="ConfigurationApplyResult.RestartRequired"/> when a live transition is unsafe.
    /// The default implementation applies the new snapshot without a restart.
    /// </summary>
    protected virtual Task<ConfigurationApplyResult> OnConfigurationChangedAsync(
        AgentConfigurationChangedContext change,
        CancellationToken cancellationToken) =>
        Task.FromResult(ConfigurationApplyResult.Applied);

    internal async Task<ConfigurationApplyResult> ApplyPlatformConfigurationAsync(
        AgentRuntimeConfiguration configuration,
        IReadOnlyList<string>? changedKeys,
        CancellationToken cancellationToken)
    {
        AgentSettings previous;
        AgentSettings current;
        lock (_settingsLock)
        {
            var definition = EnsureConfiguration();
            if (configuration.DesiredRevision > 0 && configuration.DesiredRevision <= _configurationRevision)
                return ConfigurationApplyResult.Applied;
            var validationError = ValidateSettings(definition, configuration.Settings);
            if (validationError is not null)
                throw new InvalidOperationException(validationError);
            previous = new AgentSettings(CloneSettings(_settings!));
            _settings = CloneSettings(configuration.Settings);
            _configurationRevision = configuration.DesiredRevision;
            current = new AgentSettings(CloneSettings(_settings));
        }

        var changed = changedKeys?.Distinct(StringComparer.Ordinal).ToArray()
            ?? configuration.Settings.Keys.Order(StringComparer.Ordinal).ToArray();
        return await OnConfigurationChangedAsync(new AgentConfigurationChangedContext(
            previous, current, changed, configuration.DesiredRevision, configuration.EffectiveDigest), cancellationToken);
    }

    /// <summary>Deserializes a callback payload with the SDK web JSON contract.</summary>
    protected static T? DeserializePayload<T>(JsonElement payload) =>
        payload.Deserialize<T>(SerializerOptions);

    /// <summary>Serializes a callback value with the SDK web JSON contract.</summary>
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

        var merged = MergeSettings(_settings!, settings);
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

        foreach (var field in configuration.Fields.Where(x => x.Required && IsVisible(x, merged)))
            if (!merged.TryGetValue(field.Key, out var value) || IsEmpty(value))
                return $"Setting '{field.Key}' is required.";

        foreach (var field in configuration.Fields.Where(x => !string.IsNullOrWhiteSpace(x.LessThanFieldKey)))
            if (merged.TryGetValue(field.Key, out var value) &&
                value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var dependentValue) &&
                merged.TryGetValue(field.LessThanFieldKey!, out var targetValue) &&
                targetValue.ValueKind == JsonValueKind.Number && targetValue.TryGetDecimal(out var limit) &&
                dependentValue >= limit)
                return $"Setting '{field.Key}' must be less than setting '{field.LessThanFieldKey}'.";

        return null;
    }

    private static string? ValidateField(AgentConfigurationField field, JsonElement value)
    {
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

    private static bool IsVisible(
        AgentConfigurationField field,
        IReadOnlyDictionary<string, JsonElement> settings)
    {
        if (string.IsNullOrWhiteSpace(field.VisibleWhenFieldKey))
            return true;
        return settings.TryGetValue(field.VisibleWhenFieldKey, out var controller) &&
               controller.ValueKind == JsonValueKind.String &&
               string.Equals(controller.GetString(), field.VisibleWhenValue, StringComparison.Ordinal);
    }

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
