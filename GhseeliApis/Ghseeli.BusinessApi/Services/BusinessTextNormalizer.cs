namespace Ghseeli.BusinessApi.Services;

internal static class BusinessTextNormalizer
{
    public static string NormalizeRequired(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    public static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
