using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>A deterministic, portable export of canonical infrastructure state.</summary>
public sealed record InfrastructureStateExport(string Json, string Yaml, string ContentHash);

/// <summary>Produces stable JSON and YAML-1.2-compatible exports with lexically ordered object keys.</summary>
public static class InfrastructureStateSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static InfrastructureStateExport Export<T>(T value)
    {
        var element = JsonSerializer.SerializeToElement(value, Options);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, element);
        var json = Encoding.UTF8.GetString(stream.ToArray());
        // JSON is a valid YAML 1.2 document, preserving the exact canonical representation.
        var yaml = $"---\n{json}\n";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new(json, yaml, hash);
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
