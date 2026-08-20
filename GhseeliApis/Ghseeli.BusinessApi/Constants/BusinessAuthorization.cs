namespace Ghseeli.BusinessApi.Constants;

public static class BusinessRoles
{
    public const string Owner = "Owner";
    public const string Employee = "Employee";
    public const string Admin = "Admin";

    public static readonly string[] All = [Owner, Employee, Admin];
}

public static class BusinessPolicies
{
    public const string BusinessMember = "BusinessMember";
    public const string OwnerOrAdmin = "OwnerOrAdmin";
    public const string Step5TemporaryInternalOwnerOrAdmin =
        "Step5TemporaryInternalOwnerOrAdmin";
}

public static class BusinessClaimTypes
{
    public const string CompanyId = "business_company_id";
}
