namespace CSweet.Agent.SDK;

/// <summary>Bounded strong HTTP entity tags only; never wildcards, lists or arbitrary headers.</summary>
public static class ConnectorEntityTag
{
    public static string RequireStrong(string value)
    {
        if (value is null || value.Length is < 3 or > 256 || value[0] != '"' || value[^1] != '"' ||
            value.AsSpan(1, value.Length - 2).ContainsAnyExceptInRange((char)0x21, (char)0x7e) || value.AsSpan(1, value.Length - 2).Contains('"'))
            throw new ArgumentException("A single bounded strong quoted entity tag is required.", nameof(value));
        return value;
    }
}
