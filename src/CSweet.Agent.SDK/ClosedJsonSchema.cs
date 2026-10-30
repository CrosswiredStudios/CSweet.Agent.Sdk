using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>
/// A deliberately small, closed and bounded JSON Schema subset shared by package review and trusted host
/// dispatch. Review uses <see cref="ValidateDefinition"/>; the host validates every remote input and output
/// with <see cref="Validate"/> and checks remote tool listings with <see cref="IsCompatibleRemoteInput"/>.
/// Diagnostics name schema paths only and never include instance values.
/// </summary>
public static class ClosedJsonSchema
{
    public const int MaximumDepth = 8;
    public const int MaximumStringLength = 4096;
    public const int MaximumArrayItems = 100;
    public const int MaximumProperties = 64;
    public const int MaximumEnumValues = 64;

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "type", "properties", "required", "items", "additionalProperties", "maxLength", "minLength",
        "maxItems", "minItems", "minimum", "maximum", "enum", "format", "description"
    };
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
        { "object", "array", "string", "number", "integer", "boolean", "null" };

    /// <summary>Checks that <paramref name="schema"/> stays inside the closed, bounded subset.</summary>
    public static void ValidateDefinition(JsonElement schema, string path, ICollection<string> errors, bool requireObjectRoot = false)
    {
        ArgumentNullException.ThrowIfNull(errors);
        Definition(schema, path, errors, 0);
        if (requireObjectRoot && (schema.ValueKind != JsonValueKind.Object || !TypeSet(schema).SetEquals(["object"])))
            errors.Add($"{path} must be a non-nullable object schema.");
    }

    /// <summary>
    /// Returns null when <paramref name="instance"/> conforms, otherwise the first failing schema path.
    /// Inputs reject control characters; provider outputs may carry them and are escaped by consumers.
    /// </summary>
    public static string? Validate(JsonElement schema, JsonElement instance, bool rejectControlCharacters = true) =>
        Instance(schema, instance, "$", 0, rejectControlCharacters);

    /// <summary>
    /// True when a remote MCP tool's advertised input schema declares exactly the reviewed property names with the
    /// same non-null types, and requires nothing the reviewed schema leaves optional. Descriptions, defaults and
    /// nullability are ignored.
    /// </summary>
    public static bool IsCompatibleRemoteInput(JsonElement reviewed, JsonElement remote)
    {
        if (remote.ValueKind != JsonValueKind.Object || !TypeSet(remote).SetEquals(["object"])) return false;
        var reviewedProperties = Properties(reviewed);
        var remoteProperties = Properties(remote);
        if (reviewedProperties is null || remoteProperties is null || !Unique(remote, "properties")) return false;
        if (!reviewedProperties.Keys.Order(StringComparer.Ordinal).SequenceEqual(remoteProperties.Keys.Order(StringComparer.Ordinal)))
            return false;
        // Servers advertise nullability inconsistently; the host enforces the reviewed schema (including null) on input.
        foreach (var (name, declared) in reviewedProperties)
            if (!NonNull(TypeSet(declared)).SetEquals(NonNull(TypeSet(remoteProperties[name])))) return false;
        var reviewedRequired = Required(reviewed);
        return Required(remote).All(reviewedRequired.Contains);
    }

    private static void Definition(JsonElement schema, string path, ICollection<string> errors, int depth)
    {
        if (depth > MaximumDepth) { errors.Add($"{path} exceeds the maximum schema depth of {MaximumDepth}."); return; }
        if (schema.ValueKind != JsonValueKind.Object) { errors.Add($"{path} must be a schema object."); return; }
        if (!Unique(schema)) { errors.Add($"{path} contains duplicate keywords."); return; }
        foreach (var keyword in schema.EnumerateObject())
            if (!Keywords.Contains(keyword.Name)) errors.Add($"{path} uses unsupported keyword '{keyword.Name}'.");

        var types = TypeSet(schema);
        if (!schema.TryGetProperty("type", out var typeElement) || types.Count == 0 || !types.All(Types.Contains) ||
            typeElement.ValueKind == JsonValueKind.Array && types.Count != typeElement.GetArrayLength())
        { errors.Add($"{path} requires a supported type or unique type list."); return; }
        if (types.Count(t => t != "null") != 1)
            errors.Add($"{path} must declare exactly one non-null type.");

        if (types.Contains("object"))
        {
            if (!schema.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False)
                errors.Add($"{path} objects must set additionalProperties to false.");
            var properties = Properties(schema);
            if (properties is null || !Unique(schema, "properties")) errors.Add($"{path} objects require unique properties.");
            else if (properties.Count > MaximumProperties) errors.Add($"{path} declares more than {MaximumProperties} properties.");
            else foreach (var (name, child) in properties)
            {
                if (name.Length is 0 or > 64 || name.Any(char.IsControl)) errors.Add($"{path} has an invalid property name.");
                Definition(child, $"{path}.{name}", errors, depth + 1);
            }
            if (schema.TryGetProperty("required", out var required))
            {
                if (required.ValueKind != JsonValueKind.Array || required.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String) ||
                    required.EnumerateArray().Select(x => x.GetString()).Distinct(StringComparer.Ordinal).Count() != required.GetArrayLength() ||
                    properties is not null && required.EnumerateArray().Any(x => !properties.ContainsKey(x.GetString()!)))
                    errors.Add($"{path} required must list unique declared properties.");
            }
        }
        else if (schema.TryGetProperty("properties", out _) || schema.TryGetProperty("required", out _) || schema.TryGetProperty("additionalProperties", out _))
            errors.Add($"{path} uses object keywords on a non-object type.");

        if (types.Contains("array"))
        {
            if (!PositiveInteger(schema, "maxItems", out var maxItems) || maxItems > MaximumArrayItems)
                errors.Add($"{path} arrays require maxItems from 1 to {MaximumArrayItems}.");
            if (!schema.TryGetProperty("items", out var items)) errors.Add($"{path} arrays require an items schema.");
            else Definition(items, $"{path}[]", errors, depth + 1);
        }
        else if (schema.TryGetProperty("items", out _) || schema.TryGetProperty("maxItems", out _) || schema.TryGetProperty("minItems", out _))
            errors.Add($"{path} uses array keywords on a non-array type.");

        if (types.Contains("string"))
        {
            var uuid = schema.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String && format.GetString() == "uuid";
            if (schema.TryGetProperty("format", out _) && !uuid) errors.Add($"{path} supports only the uuid string format.");
            var bounded = PositiveInteger(schema, "maxLength", out var maxLength) && maxLength <= MaximumStringLength;
            var hasEnum = schema.TryGetProperty("enum", out _);
            if (!bounded && !uuid && !hasEnum) errors.Add($"{path} strings require maxLength from 1 to {MaximumStringLength}, format uuid, or enum.");
            if (schema.TryGetProperty("maxLength", out _) && !bounded) errors.Add($"{path} maxLength must be from 1 to {MaximumStringLength}.");
        }
        else if (schema.TryGetProperty("maxLength", out _) || schema.TryGetProperty("minLength", out _) || schema.TryGetProperty("format", out _))
            errors.Add($"{path} uses string keywords on a non-string type.");

        if (schema.TryGetProperty("enum", out var values))
        {
            if (!types.Contains("string") || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is 0 or > MaximumEnumValues ||
                values.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String || x.GetString()!.Length is 0 or > 128) ||
                values.EnumerateArray().Select(x => x.GetString()).Distinct(StringComparer.Ordinal).Count() != values.GetArrayLength())
                errors.Add($"{path} enum must be 1 to {MaximumEnumValues} unique strings of at most 128 characters.");
        }

        var numeric = types.Contains("number") || types.Contains("integer");
        foreach (var bound in new[] { "minimum", "maximum" })
            if (schema.TryGetProperty(bound, out var value) && (!numeric || value.ValueKind != JsonValueKind.Number || !double.IsFinite(value.GetDouble())))
                errors.Add($"{path} {bound} requires a finite number on a numeric type.");
        if (numeric && schema.TryGetProperty("minimum", out var min) && schema.TryGetProperty("maximum", out var max) &&
            min.ValueKind == JsonValueKind.Number && max.ValueKind == JsonValueKind.Number && min.GetDouble() > max.GetDouble())
            errors.Add($"{path} minimum exceeds maximum.");
        if (schema.TryGetProperty("minLength", out var minLength) && (minLength.ValueKind != JsonValueKind.Number || !minLength.TryGetInt32(out var ml) || ml < 0))
            errors.Add($"{path} minLength must be a non-negative integer.");
        if (schema.TryGetProperty("minItems", out var minItems) && (minItems.ValueKind != JsonValueKind.Number || !minItems.TryGetInt32(out var mi) || mi < 0))
            errors.Add($"{path} minItems must be a non-negative integer.");
        if (schema.TryGetProperty("description", out var description) && (description.ValueKind != JsonValueKind.String || description.GetString()!.Length > 512))
            errors.Add($"{path} description must be a string of at most 512 characters.");
    }

    private static string? Instance(JsonElement schema, JsonElement value, string path, int depth, bool strict)
    {
        if (depth > MaximumDepth || schema.ValueKind != JsonValueKind.Object) return path;
        var types = TypeSet(schema);
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                return types.Contains("null") ? null : path;
            case JsonValueKind.True or JsonValueKind.False:
                return types.Contains("boolean") ? null : path;
            case JsonValueKind.String:
            {
                if (!types.Contains("string")) return path;
                var text = value.GetString()!;
                if (strict && text.Any(char.IsControl)) return path;
                if (schema.TryGetProperty("maxLength", out var maxLength) && text.Length > maxLength.GetInt32()) return path;
                if (schema.TryGetProperty("minLength", out var minLength) && text.Length < minLength.GetInt32()) return path;
                if (schema.TryGetProperty("format", out var format) && format.GetString() == "uuid" && !Guid.TryParseExact(text, "D", out _)) return path;
                if (schema.TryGetProperty("enum", out var values) && !values.EnumerateArray().Any(x => x.GetString() == text)) return path;
                return text.Length > MaximumStringLength ? path : null;
            }
            case JsonValueKind.Number:
            {
                if (!value.TryGetDouble(out var number) || !double.IsFinite(number)) return path;
                var integral = value.TryGetInt64(out _);
                if (!(types.Contains("number") || types.Contains("integer") && integral)) return path;
                if (schema.TryGetProperty("minimum", out var min) && number < min.GetDouble()) return path;
                if (schema.TryGetProperty("maximum", out var max) && number > max.GetDouble()) return path;
                return null;
            }
            case JsonValueKind.Array:
            {
                if (!types.Contains("array") || !schema.TryGetProperty("items", out var items)) return path;
                var count = value.GetArrayLength();
                if (schema.TryGetProperty("maxItems", out var maxItems) && count > maxItems.GetInt32() || count > MaximumArrayItems) return path;
                if (schema.TryGetProperty("minItems", out var minItems) && count < minItems.GetInt32()) return path;
                var index = 0;
                foreach (var item in value.EnumerateArray())
                    if (Instance(items, item, $"{path}[{index++}]", depth + 1, strict) is { } failure) return failure;
                return null;
            }
            case JsonValueKind.Object:
            {
                if (!types.Contains("object") || Properties(schema) is not { } properties) return path;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!seen.Add(property.Name) || !properties.TryGetValue(property.Name, out var child)) return path;
                    if (Instance(child, property.Value, $"{path}.{property.Name}", depth + 1, strict) is { } failure) return failure;
                }
                return Required(schema).All(seen.Contains) ? null : path;
            }
            default:
                return path;
        }
    }

    private static HashSet<string> TypeSet(JsonElement schema)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out var type)) return set;
        if (type.ValueKind == JsonValueKind.String) set.Add(type.GetString()!);
        else if (type.ValueKind == JsonValueKind.Array)
            foreach (var item in type.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) set.Add(item.GetString()!);
        return set;
    }

    private static HashSet<string> NonNull(HashSet<string> types)
    {
        types.Remove("null");
        return types;
    }

    private static Dictionary<string, JsonElement>? Properties(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object) return null;
        if (!schema.TryGetProperty("properties", out var properties)) return [];
        if (properties.ValueKind != JsonValueKind.Object) return null;
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in properties.EnumerateObject()) result.TryAdd(property.Name, property.Value);
        return result;
    }

    private static HashSet<string> Required(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array
            ? required.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal)
            : [];

    private static bool Unique(JsonElement element, string? child = null)
    {
        if (child is not null && (!element.TryGetProperty(child, out element) || element.ValueKind != JsonValueKind.Object)) return child == "properties";
        var names = element.EnumerateObject().Select(x => x.Name).ToArray();
        return names.Distinct(StringComparer.Ordinal).Count() == names.Length;
    }

    private static bool PositiveInteger(JsonElement schema, string name, out int value)
    {
        value = 0;
        return schema.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value) && value >= 1;
    }
}
