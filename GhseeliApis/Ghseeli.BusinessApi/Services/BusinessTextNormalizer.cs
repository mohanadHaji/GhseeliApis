using System.Globalization;

namespace Ghseeli.BusinessApi.Services;

internal static class BusinessTextNormalizer
{
    public static string NormalizeRequired(string? value)
    {
        return NormalizeOptional(value) ?? string.Empty;
    }

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

    public static bool HasMeaningfulText(string? value)
    {
        return NormalizeOptional(value) is not null;
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
