namespace GhseeliApis.Constants;

/// <summary>
/// Application role constants for authorization
/// </summary>
public static class AppRoles
{
    /// <summary>
    /// Regular user role - can book services, manage their vehicles and addresses
    /// </summary>
    public const string User = "User";

    /// <summary>
    /// Administrator role - full system access
    /// </summary>
    public const string Admin = "Admin";
}
