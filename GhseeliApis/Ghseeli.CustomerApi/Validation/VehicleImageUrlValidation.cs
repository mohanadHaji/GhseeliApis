namespace GhseeliApis.Validation;

internal static class VehicleImageUrlValidation
{
    public const int MaximumLength = 500;

    public static bool IsValid(string? value)
    {
        if (value is null)
        {
            return true;
        }

        return value.Length <= MaximumLength &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            !string.IsNullOrWhiteSpace(uri.Host);
    }
}
