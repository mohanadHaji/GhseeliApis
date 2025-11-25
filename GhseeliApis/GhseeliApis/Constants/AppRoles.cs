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
    /// Company role - can manage service offerings, view and update bookings
    /// </summary>
    public const string Company = "Company";

    /// <summary>
    /// Administrator role - full system access
    /// </summary>
    public const string Admin = "Admin";

    /// <summary>
    /// Combined roles for authorization policies
    /// </summary>
    public const string UserOrCompany = "User,Company";
    public const string CompanyOrAdmin = "Company,Admin";
    public const string All = "User,Company,Admin";
}
