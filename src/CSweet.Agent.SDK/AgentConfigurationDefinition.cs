using System.Text.Json;
namespace CSweet.Agent.SDK;

public sealed class AgentConfigurationDefinition
{
    internal AgentConfigurationDefinition(
        IReadOnlyList<AgentConfigurationField> fields,
        IReadOnlyDictionary<string, JsonElement> defaultSettings)
    {
        Fields = fields;
        DefaultSettings = defaultSettings;
    }

    public IReadOnlyList<AgentConfigurationField> Fields { get; }

    public IReadOnlyDictionary<string, JsonElement> DefaultSettings { get; }
}

public sealed class AgentConfigurationBuilder
{
    private readonly List<AgentConfigurationField> _fields = [];
    private readonly Dictionary<string, JsonElement> _defaultSettings = new(StringComparer.Ordinal);

    public AgentConfigurationBuilder LlmProvider(
        string key,
        string label,
        bool required = false,
        string? description = null,
        string defaultValue = "",
        string? visibleWhenFieldKey = null,
        string? visibleWhenValue = null) =>
        AddTextLikeField(
            key,
            label,
            AgentConfigurationFieldTypes.LlmProvider,
            required,
            description,
            placeholder: null,
            defaultValue,
            visibleWhenFieldKey,
            visibleWhenValue);

    public AgentConfigurationBuilder LlmModel(
        string key,
        string label,
        string dependsOnFieldKey,
        bool required = false,
        string? description = null,
        string defaultValue = "",
        string? visibleWhenFieldKey = null,
        string? visibleWhenValue = null) =>
        AddField(
            new AgentConfigurationField(
                key,
                label,
                AgentConfigurationFieldTypes.LlmModel,
                required,
                description,
                DependsOnFieldKey: dependsOnFieldKey,
                VisibleWhenFieldKey: visibleWhenFieldKey,
                VisibleWhenValue: visibleWhenValue),
            defaultValue);

    public AgentConfigurationBuilder Select(
        string key,
        string label,
        IEnumerable<AgentConfigurationOption> options,
        bool required = false,
        string? description = null,
        string? defaultValue = null,
        string? visibleWhenFieldKey = null,
        string? visibleWhenValue = null) =>
        AddField(
            new AgentConfigurationField(
                key,
                label,
                AgentConfigurationFieldTypes.Select,
                required,
                description,
                Options: options.ToList(),
                VisibleWhenFieldKey: visibleWhenFieldKey,
                VisibleWhenValue: visibleWhenValue),
            defaultValue ?? options.FirstOrDefault()?.Value ?? string.Empty);

    public AgentConfigurationBuilder Boolean(
        string key,
        string label,
        bool required = false,
        string? description = null,
        bool defaultValue = false,
        string? visibleWhenFieldKey = null,
        string? visibleWhenValue = null) =>
        AddField(
            new AgentConfigurationField(
                key,
                label,
                AgentConfigurationFieldTypes.Boolean,
                required,
                description,
                VisibleWhenFieldKey: visibleWhenFieldKey,
                VisibleWhenValue: visibleWhenValue),
            defaultValue);

    public AgentConfigurationBuilder Number(
        string key,
        string label,
        bool required = false,
        string? description = null,
        decimal? minimum = null,
        decimal? maximum = null,
        decimal? step = null,
        decimal? defaultValue = null,
        string? lessThanFieldKey = null,
        string? visibleWhenFieldKey = null,
        string? visibleWhenValue = null) =>
        AddField(
            new AgentConfigurationField(
                key,
                label,
                AgentConfigurationFieldTypes.Number,
                required,
                description,
                Minimum: minimum,
                Maximum: maximum,
                Step: step,
                LessThanFieldKey: lessThanFieldKey,
                VisibleWhenFieldKey: visibleWhenFieldKey,
                VisibleWhenValue: visibleWhenValue),
            defaultValue);

    public AgentConfigurationBuilder Text(
        string key,
        string label,
        bool required = false,
        string? description = null,
        string? placeholder = null,
        string defaultValue = "",
        string? visibleWhenFieldKey = null,
        string? visibleWhenValue = null) =>
        AddTextLikeField(
            key,
            label,
            AgentConfigurationFieldTypes.Text,
            required,
            description,
            placeholder,
            defaultValue,
            visibleWhenFieldKey,
            visibleWhenValue);

    public AgentConfigurationBuilder TextArea(
        string key,
        string label,
        bool required = false,
        string? description = null,
        string? placeholder = null,
        string defaultValue = "",
        string? visibleWhenFieldKey = null,
        string? visibleWhenValue = null) =>
        AddTextLikeField(
            key,
            label,
            AgentConfigurationFieldTypes.TextArea,
            required,
            description,
            placeholder,
            defaultValue,
            visibleWhenFieldKey,
            visibleWhenValue);

    public AgentConfigurationBuilder Secret(
        string key,
        string label,
        bool required = false,
        string? description = null,
        string? placeholder = null,
        string defaultValue = "",
        string? visibleWhenFieldKey = null,
        string? visibleWhenValue = null) =>
        AddTextLikeField(
            key,
            label,
            AgentConfigurationFieldTypes.Secret,
            required,
            description,
            placeholder,
            defaultValue,
            visibleWhenFieldKey,
            visibleWhenValue);

    public AgentConfigurationDefinition Build()
    {
        var duplicateKey = _fields
            .GroupBy(field => field.Key, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateKey is not null)
        {
            throw new InvalidOperationException($"Agent configuration field '{duplicateKey}' is defined more than once.");
        }

        foreach (var field in _fields.Where(x => !string.IsNullOrWhiteSpace(x.LessThanFieldKey)))
        {
            var target = _fields.SingleOrDefault(x =>
                string.Equals(x.Key, field.LessThanFieldKey, StringComparison.Ordinal));
            if (target is null)
                throw new InvalidOperationException(
                    $"Agent configuration field '{field.Key}' references unknown less-than field '{field.LessThanFieldKey}'.");
            if (field.Type != AgentConfigurationFieldTypes.Number || target.Type != AgentConfigurationFieldTypes.Number)
                throw new InvalidOperationException(
                    $"Agent configuration less-than constraint '{field.Key}' -> '{target.Key}' requires number fields.");
        }

        foreach (var field in _fields)
        {
            var hasVisibilityKey = !string.IsNullOrWhiteSpace(field.VisibleWhenFieldKey);
            var hasVisibilityValue = !string.IsNullOrWhiteSpace(field.VisibleWhenValue);
            if (hasVisibilityKey != hasVisibilityValue)
                throw new InvalidOperationException(
                    $"Agent configuration field '{field.Key}' must declare both visibility fields together.");
            if (!hasVisibilityKey)
                continue;

            var controller = _fields.SingleOrDefault(x =>
                string.Equals(x.Key, field.VisibleWhenFieldKey, StringComparison.Ordinal));
            if (controller is null)
                throw new InvalidOperationException(
                    $"Agent configuration field '{field.Key}' references unknown visibility field '{field.VisibleWhenFieldKey}'.");
            if (ReferenceEquals(controller, field))
                throw new InvalidOperationException(
                    $"Agent configuration field '{field.Key}' cannot control its own visibility.");
            if (controller.Type == AgentConfigurationFieldTypes.Select &&
                controller.Options is { Count: > 0 } &&
                !controller.Options.Any(x => string.Equals(x.Value, field.VisibleWhenValue, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"Agent configuration field '{field.Key}' visibility value is not declared by '{controller.Key}'.");
        }

        foreach (var field in _fields)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { field.Key };
            var current = field;
            while (!string.IsNullOrWhiteSpace(current.VisibleWhenFieldKey))
            {
                if (!visited.Add(current.VisibleWhenFieldKey))
                    throw new InvalidOperationException(
                        $"Agent configuration visibility cycle includes field '{field.Key}'.");
                current = _fields.Single(x => string.Equals(x.Key, current.VisibleWhenFieldKey, StringComparison.Ordinal));
            }
        }

        return new AgentConfigurationDefinition(
            _fields.ToList(),
            CloneSettings(_defaultSettings));
    }

    private AgentConfigurationBuilder AddTextLikeField(
        string key,
        string label,
        string type,
        bool required,
        string? description,
        string? placeholder,
        string defaultValue,
        string? visibleWhenFieldKey,
        string? visibleWhenValue) =>
        AddField(
            new AgentConfigurationField(
                key,
                label,
                type,
                required,
                description,
                placeholder,
                VisibleWhenFieldKey: visibleWhenFieldKey,
                VisibleWhenValue: visibleWhenValue),
            defaultValue);

    private AgentConfigurationBuilder AddField(AgentConfigurationField field, object? defaultValue)
    {
        _fields.Add(field);
        _defaultSettings[field.Key] = JsonSerializer.SerializeToElement(defaultValue, CSweetAgentBase.SerializerOptions);
        return this;
    }

    private static Dictionary<string, JsonElement> CloneSettings(IReadOnlyDictionary<string, JsonElement> settings) =>
        settings.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
}
