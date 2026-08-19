namespace Ghseeli.BusinessApi.DTOs.Companies;

public class UpdateCompanyProfileRequest
{
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public string? ServiceAreaDescriptionAr { get; set; }
    public string? ServiceAreaDescriptionHe { get; set; }
    public string? Phone { get; set; }
}

public class CreateBranchRequest
{
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string AddressAr { get; set; } = string.Empty;
    public string? AddressHe { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class UpdateBranchRequest : CreateBranchRequest
{
    public bool IsActive { get; set; } = true;
}

public class BranchResponse
{
    public Guid Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string AddressAr { get; set; } = string.Empty;
    public string? AddressHe { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsActive { get; set; }
}

public class CompanyProfileResponse
{
    public Guid Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameHe { get; set; }
    public string? DescriptionAr { get; set; }
    public string? DescriptionHe { get; set; }
    public string? ServiceAreaDescriptionAr { get; set; }
    public string? ServiceAreaDescriptionHe { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public IReadOnlyCollection<BranchResponse> Branches { get; set; } =
        Array.Empty<BranchResponse>();
}
