using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>Reads a successful platform result into the type the agent expects.</summary>
/// <remarks>
/// The platform has already committed a successful call's effect. When its result cannot be read as
/// the expected type, the call must not be retried, because a retry repeats work that happened.
/// The failure is reported as a non-retryable <see cref="ContractViolationCode"/> so the agent stops
/// and escalates. When the platform marks a result as breaking its own output schema but it still
/// reads as the expected type, the agent continues with it.
/// </remarks>
public static class PlatformCapabilityResult
{
    /// <summary>The failure code for a saved result the agent cannot read as its expected type.</summary>
    public const string ContractViolationCode = "platform.contract_violation";

    /// <summary>
    /// Deserializes <paramref name="result"/> as <typeparamref name="T"/>. An empty result throws a
    /// validation failure; an unreadable one throws a non-retryable contract violation.
    /// </summary>
    public static T Read<T>(string capability, JsonElement result, JsonSerializerOptions options, string? emptyMessage = null)
    {
        T? value;
        try
        {
            value = result.Deserialize<T>(options);
        }
        catch (JsonException exception)
        {
            throw ContractViolation(capability, typeof(T), exception);
        }
        catch (NotSupportedException exception)
        {
            throw ContractViolation(capability, typeof(T), exception);
        }
        return value ?? throw new PlatformCapabilityException(
            capability,
            PlatformCapabilityErrorCode.ValidationFailed,
            emptyMessage ?? $"The platform capability '{capability}' returned an empty response.");
    }

    /// <summary>Returns whether a failure is a saved result the agent could not read.</summary>
    public static bool IsContractViolation(Exception exception) =>
        exception is PlatformCapabilityException { FailureCode: ContractViolationCode };

    private static PlatformCapabilityException ContractViolation(string capability, Type expected, Exception inner) =>
        new(
            capability,
            PlatformCapabilityErrorCode.ValidationFailed,
            $"The platform completed '{capability}', but its result cannot be read as {Describe(expected)}: {inner.Message} " +
            "The action has already happened, so do not retry it. Report this platform contract violation to the operator.",
            inner,
            failureCode: ContractViolationCode,
            retryable: false);

    private static string Describe(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>"
            : type.Name;
}
