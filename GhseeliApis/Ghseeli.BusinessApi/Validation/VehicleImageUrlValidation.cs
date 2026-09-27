namespace Ghseeli.BusinessApi.Validation;

internal static class VehicleImageUrlValidation
{
    public static bool IsValid(string? value)
    {
        if (value is null)
        {
            return true;
        }

        return value.Length <= 500 &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(uri.UserInfo) &&
            !string.IsNullOrWhiteSpace(uri.Host);
    }
}
