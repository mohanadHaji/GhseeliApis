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
    public const string InternalCatalogRead = "InternalCatalogRead";
    public const string InternalAppointmentValidate = "InternalAppointmentValidate";
}

public static class BusinessClaimTypes
{
    public const string CompanyId = "business_company_id";
    public const string InternalServiceId = "internal_service_id";
    public const string InternalAllowedOperation = "internal_allowed_operation";
}

public static class BusinessAuthenticationSchemes
{
    public const string Combined = "BusinessCombined";
    public const string InternalService = "InternalService";
}
