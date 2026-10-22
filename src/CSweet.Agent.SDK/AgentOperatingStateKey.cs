using System.Security.Cryptography;
using System.Text.Json;

namespace CSweet.Agent.SDK;

/// <summary>Builds stable operating-state identifiers from structured identities.</summary>
public static class AgentOperatingStateKey
{
    public const int MaximumLength = 160;
    // C-Sweet uses .NET's nonbacktracking schema validator; absolute end rejects a final newline.
    public const string Pattern = @"^[A-Za-z0-9._/:-]+\z";
    public const int MaximumScopeLength = MaximumLength - 65;

    /// <summary>
    /// Appends the full SHA-256 digest of the default System.Text.Json representation of identity.
    /// Use a versioned scope and a stable identity shape/property order. Include mutable fields only
    /// when changing them should select another checkpoint. Existing keys are never rewritten.
    /// </summary>
    public static string Create<TIdentity>(string scope, TIdentity identity)
    {
        Validate(scope);
        if (scope.Length > MaximumScopeLength)
            throw new ArgumentException($"Operating-state key scopes must not exceed {MaximumScopeLength} characters.", nameof(scope));
        ArgumentNullException.ThrowIfNull(identity);
        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity)));
        return $"{scope}:{digest}";
    }

    public static bool IsValid(string? key) => !string.IsNullOrEmpty(key) && key.Length <= MaximumLength &&
        key.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '/' or ':' or '-');

    public static void Validate(string key)
    {
        if (!IsValid(key))
            throw new ArgumentException($"Operating-state keys require 1-{MaximumLength} ASCII letters, digits, or . _ / : - characters.", nameof(key));
    }
}
