using System.Text.RegularExpressions;
using Ghseeli.BusinessApi.Services;

namespace Ghseeli.BusinessApi.Validation;

internal static partial class CategoryPresentationValidation
{
    public static bool IsValidImageUrl(string? value)
    {
        var normalized = BusinessTextNormalizer.NormalizeOptional(value);
        if (normalized is null)
        {
            return true;
        }

        return normalized.Length <= 500 &&
            Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            !string.IsNullOrWhiteSpace(uri.Host);
    }

    public static bool IsValidColorHex(string? value)
    {
        var normalized = NormalizeColorHex(value);
        return normalized is null || ColorHexPattern().IsMatch(normalized);
    }

    public static string? NormalizeColorHex(string? value) =>
        BusinessTextNormalizer.NormalizeOptional(value)?.ToUpperInvariant();

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorHexPattern();
}
