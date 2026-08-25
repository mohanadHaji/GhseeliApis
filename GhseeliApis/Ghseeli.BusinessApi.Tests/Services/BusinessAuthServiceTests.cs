using FluentAssertions;
using FluentValidation;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.DTOs.Auth;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services;
using Ghseeli.Common.Logging;
using Ghseeli.BusinessApi.Services.Validation.Auth;
using Ghseeli.BusinessApi.Validators.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Defines owner registration and isolated Business API authentication behavior.
/// </summary>
public class BusinessAuthServiceTests
{
    private readonly Mock<UserManager<BusinessUser>> _userManager;
    private readonly Mock<SignInManager<BusinessUser>> _signInManager;
    private readonly Mock<RoleManager<IdentityRole<Guid>>> _roleManager;
    private readonly Mock<ICompanyRepository> _companyRepository;
    private readonly Mock<IAppLogger> _logger = new();
    private readonly BusinessAuthService _service;

    public BusinessAuthServiceTests()
    {
        var userStore = new Mock<IUserStore<BusinessUser>>();
        _userManager = new Mock<UserManager<BusinessUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var contextAccessor = new Mock<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<BusinessUser>>();
        _signInManager = new Mock<SignInManager<BusinessUser>>(
            _userManager.Object, contextAccessor.Object, claimsFactory.Object,
            null!, null!, null!, null!);

        var roleStore = new Mock<IRoleStore<IdentityRole<Guid>>>();
        _roleManager = new Mock<RoleManager<IdentityRole<Guid>>>(
            roleStore.Object, null!, null!, null!, null!);
        _roleManager.Setup(manager => manager.RoleExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        _companyRepository = new Mock<ICompanyRepository>();

        var settings = new Dictionary<string, string?>
        {
            ["BusinessJwtSettings:SecretKey"] = "BusinessTestSecretKey_Minimum32Characters",
            ["BusinessJwtSettings:Issuer"] = "Ghseeli.BusinessApi.Tests",
            ["BusinessJwtSettings:Audience"] = "Ghseeli.BusinessClients.Tests",
            ["BusinessJwtSettings:ExpirationMinutes"] = "60"
        };

        _service = new BusinessAuthService(
            _userManager.Object,
            _signInManager.Object,
            _roleManager.Object,
            _companyRepository.Object,
            new BusinessAuthRequestValidator(
                new RegisterOwnerRequestValidator(),
                new BusinessLoginRequestValidator()),
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            _logger.Object);
    }

    [Fact]
    public async Task RegisterOwnerAsync_CreatesOwnerCompanyAssignmentAndBusinessToken()
    {
        var request = new RegisterOwnerRequest
        {
            Email = "owner@example.com",
            Password = "Password1",
            FullName = "Owner Name",
            CompanyNameAr = "شركة غسيلي"
        };
        Company? capturedCompany = null;
        BusinessUserAssignment? capturedAssignment = null;

        _userManager.Setup(manager => manager.FindByEmailAsync(request.Email))
            .ReturnsAsync((BusinessUser?)null);
        _userManager.Setup(manager => manager.CreateAsync(It.IsAny<BusinessUser>(), request.Password))
            .ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(manager => manager.AddToRoleAsync(It.IsAny<BusinessUser>(), BusinessRoles.Owner))
            .ReturnsAsync(IdentityResult.Success);
        _companyRepository
            .Setup(repository => repository.CreateForOwnerAsync(
                It.IsAny<Company>(), It.IsAny<BusinessUserAssignment>()))
            .Callback<Company, BusinessUserAssignment>((company, assignment) =>
            {
                capturedCompany = company;
                capturedAssignment = assignment;
            })
            .Returns(Task.CompletedTask);

        var result = await _service.RegisterOwnerAsync(request);

        result.Email.Should().Be(request.Email);
        result.CompanyId.Should().Be(capturedCompany!.Id);
        capturedCompany.NameAr.Should().Be(request.CompanyNameAr);
        capturedCompany.NameHe.Should().BeNull();
        capturedCompany.BusinessVerticals.Should().ContainSingle(vertical =>
            vertical.BusinessVerticalId == BusinessVerticalDefaults.CarWashId &&
            vertical.IsPrimary &&
            vertical.IsActive);
        capturedAssignment!.Role.Should().Be(BusinessMembershipRole.Owner);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        token.Issuer.Should().Be("Ghseeli.BusinessApi.Tests");
        token.Audiences.Should().Contain("Ghseeli.BusinessClients.Tests");
        token.Claims.Should().Contain(claim =>
            claim.Type == BusinessClaimTypes.CompanyId &&
            claim.Value == capturedCompany.Id.ToString());
        token.Claims.Should().Contain(claim =>
            claim.Type == ClaimTypes.Role && claim.Value == BusinessRoles.Owner);
        _logger.Verify(logger => logger.LogInfo(
            It.Is<string>(message =>
                message.Contains("registration succeeded", StringComparison.OrdinalIgnoreCase) &&
                message.Contains(capturedCompany.Id.ToString(), StringComparison.OrdinalIgnoreCase))),
            Times.Once);
    }

    [Fact]
    public async Task RegisterOwnerAsync_WhenEmailExists_DoesNotCreateCompany()
    {
        var request = new RegisterOwnerRequest
        {
            Email = "existing@example.com",
            Password = "Password1",
            FullName = "Existing Owner",
            CompanyNameAr = "شركة",
            CompanyNameHe = "חברה"
        };
        _userManager.Setup(manager => manager.FindByEmailAsync(request.Email))
            .ReturnsAsync(new BusinessUser());

        var action = () => _service.RegisterOwnerAsync(request);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already exists*");
        _companyRepository.Verify(
            repository => repository.CreateForOwnerAsync(
                It.IsAny<Company>(), It.IsAny<BusinessUserAssignment>()),
            Times.Never);
        _logger.Verify(logger => logger.LogWarning(
            It.Is<string>(message =>
                message.Contains("registration rejected", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("already exists", StringComparison.OrdinalIgnoreCase))),
            Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WhenUserIsInactive_RejectsLogin()
    {
        var user = new BusinessUser
        {
            Id = Guid.NewGuid(),
            Email = "inactive@example.com",
            UserName = "inactive@example.com",
            IsActive = false
        };
        _userManager.Setup(manager => manager.FindByEmailAsync(user.Email))
            .ReturnsAsync(user);

        var action = () => _service.LoginAsync(new BusinessLoginRequest
        {
            Email = user.Email,
            Password = "Password1"
        });

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*inactive*");
        _signInManager.Verify(
            manager => manager.CheckPasswordSignInAsync(
                It.IsAny<BusinessUser>(), It.IsAny<string>(), It.IsAny<bool>()),
            Times.Never);
        _logger.Verify(logger => logger.LogWarning(
            It.Is<string>(message =>
                message.Contains("inactive", StringComparison.OrdinalIgnoreCase) &&
                message.Contains(user.Id.ToString(), StringComparison.OrdinalIgnoreCase))),
            Times.Once);
    }

    [Fact]
    public async Task LoginAsync_WhenCredentialsAreValid_ReturnsTokenAndLogsInfo()
    {
        var user = new BusinessUser
        {
            Id = Guid.NewGuid(),
            Email = "owner@example.com",
            UserName = "owner@example.com",
            FullName = "Owner",
            IsActive = true
        };
        var companyId = Guid.NewGuid();

        _userManager.Setup(manager => manager.FindByEmailAsync(user.Email))
            .ReturnsAsync(user);
        _signInManager.Setup(manager => manager.CheckPasswordSignInAsync(
                user,
                "Password1",
                true))
            .ReturnsAsync(SignInResult.Success);
        _userManager.Setup(manager => manager.GetRolesAsync(user))
            .ReturnsAsync([BusinessRoles.Owner]);
        _companyRepository.Setup(repository => repository.GetAssignmentForUserAsync(user.Id))
            .ReturnsAsync(new BusinessUserAssignment
            {
                UserId = user.Id,
                CompanyId = companyId,
                Role = BusinessMembershipRole.Owner,
                IsActive = true
            });

        var result = await _service.LoginAsync(new BusinessLoginRequest
        {
            Email = user.Email,
            Password = "Password1"
        });

        result.CompanyId.Should().Be(companyId);
        _logger.Verify(logger => logger.LogInfo(
            It.Is<string>(message =>
                message.Contains("login succeeded", StringComparison.OrdinalIgnoreCase) &&
                message.Contains(user.Id.ToString(), StringComparison.OrdinalIgnoreCase))),
            Times.Once);
    }

    [Fact]
    public async Task RegisterOwnerAsync_WhenCompanyPersistenceFails_LogsRollbackError()
    {
        var request = new RegisterOwnerRequest
        {
            Email = "owner@example.com",
            Password = "Password1",
            FullName = "Owner Name",
            CompanyNameAr = "شركة غسيلي",
            CompanyNameHe = "חברת גסילי"
        };
        var exception = new InvalidOperationException("Database write failed");

        _userManager.Setup(manager => manager.FindByEmailAsync(request.Email))
            .ReturnsAsync((BusinessUser?)null);
        _userManager.Setup(manager => manager.CreateAsync(It.IsAny<BusinessUser>(), request.Password))
            .ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(manager => manager.AddToRoleAsync(It.IsAny<BusinessUser>(), BusinessRoles.Owner))
            .ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(manager => manager.DeleteAsync(It.IsAny<BusinessUser>()))
            .ReturnsAsync(IdentityResult.Success);
        _companyRepository.Setup(repository => repository.CreateForOwnerAsync(
                It.IsAny<Company>(),
                It.IsAny<BusinessUserAssignment>()))
            .ThrowsAsync(exception);

        var action = () => _service.RegisterOwnerAsync(request);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Database write failed");
        _logger.Verify(logger => logger.LogError(
            It.Is<string>(message =>
                message.Contains("rolling back", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("registration failed", StringComparison.OrdinalIgnoreCase)),
            exception),
            Times.Once);
    }

    [Fact]
    public async Task RegisterOwnerAsync_WhenArabicCompanyNameIsMissing_ThrowsValidationException()
    {
        var request = new RegisterOwnerRequest
        {
            Email = "owner@example.com",
            Password = "Password1",
            FullName = "Owner Name",
            CompanyNameAr = "   "
        };

        var action = () => _service.RegisterOwnerAsync(request);

        await action.Should().ThrowAsync<ValidationException>()
            .Where(exception => exception.Errors.Any(error => error.PropertyName == nameof(RegisterOwnerRequest.CompanyNameAr)));
        _userManager.Verify(manager => manager.FindByEmailAsync(It.IsAny<string>()), Times.Never);
    }
}
