using System.Globalization;

namespace GhseeliApis.Services.Configuration;

internal static class ConfigurationTextNormalizer
{
    public static string NormalizeRequired(string value) =>
        NormalizeOptional(value) ?? string.Empty;

    public static string? NormalizeOptional(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.All(IsIgnorableTextCharacter))
        {
            return null;
        }

        return trimmed;
    }

    private static bool IsIgnorableTextCharacter(char value)
    {
        if (char.IsWhiteSpace(value))
        {
            return true;
        }

        return CharUnicodeInfo.GetUnicodeCategory(value) is
            UnicodeCategory.Control or
            UnicodeCategory.Format or
            UnicodeCategory.Surrogate or
            UnicodeCategory.OtherNotAssigned;
    }
}
