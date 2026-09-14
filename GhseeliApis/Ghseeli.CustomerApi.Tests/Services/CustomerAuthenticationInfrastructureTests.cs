using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Auth;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Auth;
using GhseeliApis.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using System.Net.Mail;

namespace GhseeliApis.Tests.Services;

/// <summary>
/// Tests customer OTP and refresh-token security behavior.
/// </summary>
public sealed class CustomerAuthenticationInfrastructureTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 14, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OtpGenerator_AlwaysReturnsSixDigits()
    {
        var generator = new OtpCodeGenerator();

        Enumerable.Range(0, 100)
            .Select(_ => generator.Generate())
            .Should().OnlyContain(code =>
                code.Length == 6 && code.All(char.IsDigit));
    }

    [Fact]
    public void OtpHash_IsSaltedAndMatchesOnlyTheIssuedCode()
    {
        var firstSalt = OtpCodeHasher.CreateSalt();
        var secondSalt = OtpCodeHasher.CreateSalt();
        var first = OtpCodeHasher.Hash("123456", firstSalt);
        var second = OtpCodeHasher.Hash("123456", secondSalt);

        first.Should().NotEqual(second);
        OtpCodeHasher.Matches("123456", firstSalt, first).Should().BeTrue();
        OtpCodeHasher.Matches("654321", firstSalt, first).Should().BeFalse();
    }

    [Fact]
    [Trait("ScenarioId", "STEP23-OTP-REQUEST-003")]
    public async Task RequestAsync_StoresOnlyHashAndInvalidatesPreviousCode()
    {
        await using var context = CreateContext();
        var users = CreateUserManager();
        users.Setup(value => value.NormalizeEmail(It.IsAny<string>()))
            .Returns("USER@EXAMPLE.COM");
        var sender = new RecordingOtpSender();
        var generator = new Mock<IOtpCodeGenerator>();
        generator.SetupSequence(value => value.Generate())
            .Returns("123456")
            .Returns("654321");
        var service = CreateOtpService(context, users, sender, generator.Object);

        await service.RequestAsync("user@example.com", default);
        await service.RequestAsync("user@example.com", default);

        var challenges = await context.CustomerOtpChallenges
            .OrderBy(value => value.CreatedAtUtc)
            .ToListAsync();
        challenges.Should().HaveCount(2);
        challenges[0].ConsumedAtUtc.Should().Be(Now);
        challenges[1].ConsumedAtUtc.Should().BeNull();
        challenges.Should().OnlyContain(value =>
            !value.CodeHash.SequenceEqual(System.Text.Encoding.ASCII.GetBytes("123456")) &&
            !value.CodeHash.SequenceEqual(System.Text.Encoding.ASCII.GetBytes("654321")));
        sender.Deliveries.Should().Equal(
            ("user@example.com", "123456"),
            ("user@example.com", "654321"));
    }

    [Fact]
    public async Task ConfirmAsync_CodeForDifferentEmail_IsRejected()
    {
        await using var context = CreateContext();
        AddChallenge(context, "FIRST@EXAMPLE.COM", "123456");
        await context.SaveChangesAsync();
        var users = CreateUserManager();
        users.Setup(value => value.NormalizeEmail("second@example.com"))
            .Returns("SECOND@EXAMPLE.COM");
        var service = CreateOtpService(
            context,
            users,
            new RecordingOtpSender(),
            Mock.Of<IOtpCodeGenerator>());

        var action = () => service.ConfirmAsync(
            "second@example.com",
            "123456",
            default);

        await action.Should().ThrowAsync<CustomerOtpException>()
            .Where(value => value.Code == CustomerAuthProblemCodes.OtpInvalid);
    }

    [Fact]
    [Trait("ScenarioId", "STEP23-OTP-REQUEST-004")]
    public async Task RequestAsync_DeliveryFailureConsumesChallenge()
    {
        await using var context = CreateContext();
        var users = CreateUserManager();
        users.Setup(value => value.NormalizeEmail(It.IsAny<string>()))
            .Returns("USER@EXAMPLE.COM");
        var service = CreateOtpService(
            context,
            users,
            new FailingOtpSender(),
            Mock.Of<IOtpCodeGenerator>(value => value.Generate() == "123456"));

        var action = () => service.RequestAsync("user@example.com", default);

        await action.Should().ThrowAsync<CustomerOtpException>()
            .Where(value =>
                value.Code == CustomerAuthProblemCodes.OtpDeliveryUnavailable);
        (await context.CustomerOtpChallenges.SingleAsync())
            .ConsumedAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task ConfirmAsync_MaximumIncorrectAttemptsConsumesChallenge()
    {
        await using var context = CreateContext();
        AddChallenge(context, "USER@EXAMPLE.COM", "123456");
        await context.SaveChangesAsync();
        var users = CreateUserManager();
        users.Setup(value => value.NormalizeEmail(It.IsAny<string>()))
            .Returns("USER@EXAMPLE.COM");
        var service = CreateOtpService(
            context,
            users,
            new RecordingOtpSender(),
            Mock.Of<IOtpCodeGenerator>());

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await FluentActions.Awaiting(() =>
                    service.ConfirmAsync("user@example.com", "000000", default))
                .Should().ThrowAsync<CustomerOtpException>()
                .Where(value => value.Code == CustomerAuthProblemCodes.OtpInvalid);
        }

        await FluentActions.Awaiting(() =>
                service.ConfirmAsync("user@example.com", "000000", default))
            .Should().ThrowAsync<CustomerOtpException>()
            .Where(value => value.Code == CustomerAuthProblemCodes.OtpAttemptLimit);
        var challenge = await context.CustomerOtpChallenges.SingleAsync();
        challenge.FailedAttemptCount.Should().Be(5);
        challenge.ConsumedAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task ConfirmAsync_ExistingUser_ReturnsTokensAndNotNewUser()
    {
        await using var context = CreateContext();
        AddChallenge(context, "USER@EXAMPLE.COM", "123456");
        await context.SaveChangesAsync();
        var user = User("user@example.com");
        var users = CreateUserManager();
        users.Setup(value => value.NormalizeEmail(user.Email!))
            .Returns("USER@EXAMPLE.COM");
        users.Setup(value => value.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        var auth = new Mock<IAuthService>();
        auth.Setup(value => value.GenerateJwtTokenAsync(user.Id, user.Email!, user.FullName))
            .ReturnsAsync("access-token");
        var refresh = new Mock<ICustomerRefreshTokenService>();
        refresh.Setup(value => value.IssueAsync(user.Id, default))
            .ReturnsAsync(new IssuedRefreshToken("refresh-token", Now.AddDays(30)));
        var service = CreateOtpService(
            context,
            users,
            new RecordingOtpSender(),
            Mock.Of<IOtpCodeGenerator>(),
            auth.Object,
            refresh.Object);

        var result = await service.ConfirmAsync(user.Email!, "123456", default);

        result.IsNewUser.Should().BeFalse();
        result.Token.Should().Be("access-token");
        result.RefreshToken.Should().Be("refresh-token");
        (await context.CustomerOtpChallenges.SingleAsync())
            .ConsumedAtUtc.Should().Be(Now);
    }

    [Fact]
    public async Task RefreshToken_RotatesOnceAndReuseRevokesFamily()
    {
        await using var context = CreateContext();
        var user = User("refresh@example.com");
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = new CustomerRefreshTokenService(
            context,
            new FixedTimeProvider(Now),
            Options.Create(new CustomerRefreshTokenOptions { LifetimeDays = 30 }));

        var issued = await service.IssueAsync(user.Id, default);
        var rotated = await service.RotateAsync(
            issued.Token,
            (_, _, _) => Task.FromResult("access-token"),
            default);
        var reuse = () => service.RotateAsync(
            issued.Token,
            (_, _, _) => Task.FromResult("access-token"),
            default);

        rotated.Token.Should().NotBe(issued.Token);
        await reuse.Should().ThrowAsync<CustomerRefreshTokenException>();
        var family = await context.CustomerRefreshTokens.ToListAsync();
        family.Should().HaveCount(2);
        family.Should().OnlyContain(value => value.RevokedAtUtc == Now);
        family.Should().OnlyContain(value =>
            !value.TokenHash.SequenceEqual(
                System.Text.Encoding.ASCII.GetBytes(issued.Token)));
    }

    [Fact]
    public async Task RefreshToken_AccessTokenFailure_DoesNotConsumeOriginalToken()
    {
        await using var context = CreateContext();
        var user = User("refresh-failure@example.com");
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = new CustomerRefreshTokenService(
            context,
            new FixedTimeProvider(Now),
            Options.Create(new CustomerRefreshTokenOptions { LifetimeDays = 30 }));
        var issued = await service.IssueAsync(user.Id, default);

        var failedRotation = () => service.RotateAsync(
            issued.Token,
            (_, _, _) => throw new InvalidOperationException("JWT unavailable"),
            default);

        await failedRotation.Should().ThrowAsync<InvalidOperationException>();
        context.ChangeTracker.Clear();
        var rotated = await service.RotateAsync(
            issued.Token,
            (_, _, _) => Task.FromResult("access-token"),
            default);

        rotated.AccessToken.Should().Be("access-token");
        (await context.CustomerRefreshTokens.ToListAsync()).Should().HaveCount(2);
    }

    private static CustomerOtpAuthenticationService CreateOtpService(
        ApplicationDbContext context,
        Mock<UserManager<User>> users,
        ICustomerOtpEmailSender sender,
        IOtpCodeGenerator generator,
        IAuthService? auth = null,
        ICustomerRefreshTokenService? refresh = null) =>
        new(
            context,
            users.Object,
            auth ?? Mock.Of<IAuthService>(),
            refresh ?? Mock.Of<ICustomerRefreshTokenService>(),
            sender,
            generator,
            new FixedTimeProvider(Now),
            Options.Create(new CustomerOtpOptions
            {
                LifetimeMinutes = 5,
                MaximumFailedAttempts = 5
            }),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JwtSettings:ExpirationMinutes"] = "60"
                })
                .Build(),
            Mock.Of<IAppLogger>());

    private static void AddChallenge(
        ApplicationDbContext context,
        string normalizedEmail,
        string code)
    {
        var salt = OtpCodeHasher.CreateSalt();
        context.CustomerOtpChallenges.Add(new CustomerOtpChallenge
        {
            Id = Guid.NewGuid(),
            NormalizedEmail = normalizedEmail,
            CodeSalt = salt,
            CodeHash = OtpCodeHasher.Hash(code, salt),
            CreatedAtUtc = Now,
            ExpiresAtUtc = Now.AddMinutes(5),
            DeliveredAtUtc = Now
        });
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"CustomerAuth-{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Mock<UserManager<User>> CreateUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(
            store.Object,
            null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static User User(string email) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = "Test User",
            IsActive = true
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingOtpSender : ICustomerOtpEmailSender
    {
        public List<(string Email, string Code)> Deliveries { get; } = [];

        public Task SendAsync(
            string email,
            string code,
            CancellationToken cancellationToken)
        {
            Deliveries.Add((email, code));
            return Task.CompletedTask;
        }
    }

    private sealed class FailingOtpSender : ICustomerOtpEmailSender
    {
        public Task SendAsync(
            string email,
            string code,
            CancellationToken cancellationToken) =>
            throw new SmtpException("Unavailable");
    }
}
