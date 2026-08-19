using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Auth;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Ghseeli.BusinessApi.Services;

public class BusinessAuthService : IBusinessAuthService
{
    private readonly UserManager<BusinessUser> _userManager;
    private readonly SignInManager<BusinessUser> _signInManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly ICompanyRepository _companyRepository;
    private readonly IConfiguration _configuration;

    public BusinessAuthService(
        UserManager<BusinessUser> userManager,
        SignInManager<BusinessUser> signInManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ICompanyRepository companyRepository,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _companyRepository = companyRepository;
        _configuration = configuration;
    }

    public async Task<BusinessAuthResponse> RegisterOwnerAsync(
        RegisterOwnerRequest request)
    {
        if (await _userManager.FindByEmailAsync(request.Email) != null)
        {
            throw new InvalidOperationException("A business account with this email already exists.");
        }

        foreach (var role in BusinessRoles.All)
        {
            await EnsureRoleExistsAsync(role);
        }

        var user = new BusinessUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            UserName = request.Email,
            FullName = request.FullName,
            PhoneNumber = request.PhoneNumber,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        EnsureIdentitySucceeded(createResult, "Business owner registration failed");

        var roleResult = await _userManager.AddToRoleAsync(user, BusinessRoles.Owner);
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            EnsureIdentitySucceeded(roleResult, "Business owner role assignment failed");
        }

        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = request.CompanyNameAr.Trim(),
            NameHe = request.CompanyNameHe.Trim(),
            Phone = request.PhoneNumber,
            CreatedAt = DateTime.UtcNow
        };
        var assignment = new BusinessUserAssignment
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CompanyId = company.Id,
            Role = BusinessMembershipRole.Owner,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _companyRepository.CreateForOwnerAsync(company, assignment);
        }
        catch
        {
            await _userManager.DeleteAsync(user);
            throw;
        }

        return CreateResponse(user, company.Id, [BusinessRoles.Owner]);
    }

    public async Task<BusinessAuthResponse> LoginAsync(BusinessLoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email)
            ?? throw new InvalidOperationException("Invalid email or password.");

        if (!user.IsActive)
        {
            throw new InvalidOperationException("This business account is inactive.");
        }

        var signInResult = await _signInManager.CheckPasswordSignInAsync(
            user, request.Password, lockoutOnFailure: true);
        if (!signInResult.Succeeded)
        {
            throw new InvalidOperationException("Invalid email or password.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var assignment = await _companyRepository.GetAssignmentForUserAsync(user.Id);
        return CreateResponse(user, assignment?.CompanyId, roles);
    }

    private async Task EnsureRoleExistsAsync(string role)
    {
        if (await _roleManager.RoleExistsAsync(role))
        {
            return;
        }

        var result = await _roleManager.CreateAsync(new IdentityRole<Guid>(role));
        EnsureIdentitySucceeded(result, $"Creating business role '{role}' failed");
    }

    private BusinessAuthResponse CreateResponse(
        BusinessUser user,
        Guid? companyId,
        IEnumerable<string> roles)
    {
        var roleList = roles.Distinct(StringComparer.Ordinal).ToArray();
        var settings = _configuration.GetSection("BusinessJwtSettings");
        var secretKey = settings["SecretKey"]
            ?? throw new InvalidOperationException("Business JWT secret is not configured.");
        var issuer = settings["Issuer"];
        var audience = settings["Audience"];
        var expirationMinutes = int.Parse(settings["ExpirationMinutes"] ?? "60");
        var expiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roleList.Select(role => new Claim(ClaimTypes.Role, role)));
        if (companyId.HasValue)
        {
            claims.Add(new Claim(BusinessClaimTypes.CompanyId, companyId.Value.ToString()));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new BusinessAuthResponse
        {
            UserId = user.Id,
            CompanyId = companyId,
            Email = user.Email ?? string.Empty,
            FullName = user.FullName,
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAtUtc = expiresAt,
            Roles = roleList
        };
    }

    private static void EnsureIdentitySucceeded(
        IdentityResult result,
        string message)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"{message}: {string.Join(", ", result.Errors.Select(error => error.Description))}");
        }
    }
}
