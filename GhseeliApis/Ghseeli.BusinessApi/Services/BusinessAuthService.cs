using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Auth;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.BusinessApi.Services.Validation.Auth;
using Ghseeli.Common.Logging;
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
    private readonly IBusinessAuthRequestValidator _requestValidator;
    private readonly IConfiguration _configuration;
    private readonly IAppLogger _logger;

    public BusinessAuthService(
        UserManager<BusinessUser> userManager,
        SignInManager<BusinessUser> signInManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ICompanyRepository companyRepository,
        IBusinessAuthRequestValidator requestValidator,
        IConfiguration configuration,
        IAppLogger logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _companyRepository = companyRepository;
        _requestValidator = requestValidator;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<BusinessAuthResponse> RegisterOwnerAsync(
        RegisterOwnerRequest request)
    {
        _requestValidator.Validate(request);

        var email = BusinessTextNormalizer.NormalizeRequired(request.Email);
        if (await _userManager.FindByEmailAsync(email) != null)
        {
            _logger.LogWarning(
                "Business owner registration rejected because an account already exists.");
            throw new InvalidOperationException("A business account with this email already exists.");
        }

        foreach (var role in BusinessRoles.All)
        {
            await EnsureRoleExistsAsync(role);
        }

        var utcNow = DateTime.UtcNow;
        var user = new BusinessUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            UserName = email,
            FullName = BusinessTextNormalizer.NormalizeRequired(request.FullName),
            PhoneNumber = BusinessTextNormalizer.NormalizeOptional(request.PhoneNumber),
            IsActive = true,
            CreatedAt = utcNow
        };

        var createResult = await _userManager.CreateAsync(user, request.Password);
        EnsureIdentitySucceeded(
            createResult,
            "Business owner registration failed",
            $"Business owner registration failed identity checks for user {user.Id}.");

        var roleResult = await _userManager.AddToRoleAsync(user, BusinessRoles.Owner);
        if (!roleResult.Succeeded)
        {
            _logger.LogWarning(
                $"Business owner role assignment failed for user {user.Id}. Identity codes: {FormatIdentityCodes(roleResult)}.");
            await CleanupUserAsync(user, "business owner role assignment failure");
            EnsureIdentitySucceeded(
                roleResult,
                "Business owner role assignment failed",
                $"Business owner role assignment failed for user {user.Id}.");
        }

        var company = new Company
        {
            Id = Guid.NewGuid(),
            NameAr = BusinessTextNormalizer.NormalizeRequired(request.CompanyNameAr),
            NameHe = BusinessTextNormalizer.NormalizeOptional(request.CompanyNameHe),
            Phone = BusinessTextNormalizer.NormalizeOptional(request.PhoneNumber),
            CreatedAt = utcNow
        };
        var assignment = new BusinessUserAssignment
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            CompanyId = company.Id,
            Role = BusinessMembershipRole.Owner,
            IsActive = true,
            CreatedAt = utcNow
        };

        try
        {
            await _companyRepository.CreateForOwnerAsync(company, assignment);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                $"Business owner registration failed while creating company {company.Id} for user {user.Id}. Rolling back identity user.",
                exception);
            await CleanupUserAsync(user, "business owner company persistence failure");
            throw;
        }

        _logger.LogInfo(
            $"Business owner registration succeeded for user {user.Id} and company {company.Id}.");
        return CreateResponse(user, company.Id, [BusinessRoles.Owner]);
    }

    public async Task<BusinessAuthResponse> LoginAsync(BusinessLoginRequest request)
    {
        _requestValidator.Validate(request);

        var email = BusinessTextNormalizer.NormalizeRequired(request.Email);
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            _logger.LogWarning("Business login rejected due to invalid credentials.");
            throw new InvalidOperationException("Invalid email or password.");
        }

        if (!user.IsActive)
        {
            _logger.LogWarning(
                $"Business login rejected because user {user.Id} is inactive.");
            throw new InvalidOperationException("This business account is inactive.");
        }

        var signInResult = await _signInManager.CheckPasswordSignInAsync(
            user, request.Password, lockoutOnFailure: true);
        if (!signInResult.Succeeded)
        {
            _logger.LogWarning(
                $"Business login rejected due to invalid credentials for user {user.Id}.");
            throw new InvalidOperationException("Invalid email or password.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var assignment = await _companyRepository.GetAssignmentForUserAsync(user.Id);
        _logger.LogInfo($"Business login succeeded for user {user.Id}.");
        return CreateResponse(user, assignment?.CompanyId, roles);
    }

    private async Task EnsureRoleExistsAsync(string role)
    {
        if (await _roleManager.RoleExistsAsync(role))
        {
            return;
        }

        var result = await _roleManager.CreateAsync(new IdentityRole<Guid>(role));
        if (!result.Succeeded)
        {
            _logger.LogError(
                $"Creating business role '{role}' failed. Identity codes: {FormatIdentityCodes(result)}.");
        }

        EnsureIdentitySucceeded(
            result,
            $"Creating business role '{role}' failed",
            $"Creating business role '{role}' failed.");
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

    private void EnsureIdentitySucceeded(
        IdentityResult result,
        string message,
        string logMessage)
    {
        if (!result.Succeeded)
        {
            _logger.LogWarning($"{logMessage} Identity codes: {FormatIdentityCodes(result)}.");
            throw new InvalidOperationException(
                $"{message}: {string.Join(", ", result.Errors.Select(error => error.Description))}");
        }
    }

    private async Task CleanupUserAsync(BusinessUser user, string reason)
    {
        var deleteResult = await _userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            _logger.LogError(
                $"Business identity cleanup failed after {reason} for user {user.Id}. Identity codes: {FormatIdentityCodes(deleteResult)}.");
        }
    }

    private static string FormatIdentityCodes(IdentityResult result)
    {
        var codes = result.Errors
            .Select(error => error.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return codes.Length == 0 ? "Unknown" : string.Join(", ", codes);
    }
}
