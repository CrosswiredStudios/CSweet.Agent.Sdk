using System.Text.RegularExpressions;

namespace CSweet.Agent.SDK;

/// <summary>Shared validation for optional marketplace presentation metadata.</summary>
public static partial class AgentCatalogBranding
{
    public const int MaximumUrlLength = 2048;
    public const int MaximumDescriptionLength = 2000;

    /// <summary>Only absolute HTTPS artwork without credentials is accepted.</summary>
    public static bool IsImageUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumUrlLength &&
        !value.Any(char.IsWhiteSpace) &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(uri.Host) &&
        string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>Six-digit hex colors can safely be used in a CSS custom property.</summary>
    public static bool IsAccentColor(string? value) => value is not null && AccentPattern().IsMatch(value);

    /// <summary>Missing or blank optional values preserve the marketplace defaults.</summary>
    public static IReadOnlyList<string> Validate(string? imageUrl, string? companyLogoUrl,
        string? accentColor, string? longDescription)
    {
        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(imageUrl) && !IsImageUrl(imageUrl))
            errors.Add("catalog.imageUrl must be an absolute HTTPS URL without credentials, at most 2048 characters.");
        if (!string.IsNullOrWhiteSpace(companyLogoUrl) && !IsImageUrl(companyLogoUrl))
            errors.Add("catalog.companyLogoUrl must be an absolute HTTPS URL without credentials, at most 2048 characters.");
        if (!string.IsNullOrWhiteSpace(accentColor) && !IsAccentColor(accentColor))
            errors.Add("catalog.accentColor must be a six-digit hex color such as #1C6252.");
        if (longDescription?.Length > MaximumDescriptionLength)
            errors.Add("catalog.longDescription cannot exceed 2000 characters.");
        return errors;
    }

    [GeneratedRegex(@"\A#[0-9a-fA-F]{6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex AccentPattern();
}
