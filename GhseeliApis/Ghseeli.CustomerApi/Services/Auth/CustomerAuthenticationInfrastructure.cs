using Ghseeli.Common.Logging;
using GhseeliApis.Constants;
using GhseeliApis.DTOs.Auth;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Interfaces;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using System.Data;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;

namespace GhseeliApis.Services.Auth;

public static class CustomerAuthProblemCodes
{
    public const string OtpInvalid = "otp_invalid";
    public const string OtpAttemptLimit = "otp_attempt_limit";
    public const string OtpDeliveryUnavailable = "otp_delivery_unavailable";
    public const string RefreshTokenInvalid = "refresh_token_invalid";
}

public sealed class CustomerOtpOptions
{
    public const string SectionName = "CustomerOtp";
    public int LifetimeMinutes { get; set; } = 5;
    public int MaximumFailedAttempts { get; set; } = 5;
}

public sealed class CustomerRefreshTokenOptions
{
    public const string SectionName = "CustomerRefreshTokens";
    public int LifetimeDays { get; set; } = 30;
}

public sealed class CustomerSmtpOptions
{
    public const string SectionName = "CustomerSmtp";
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Ghseeli";
}

public sealed class CustomerOtpOptionsValidator :
    IValidateOptions<CustomerOtpOptions>
{
    public ValidateOptionsResult Validate(string? name, CustomerOtpOptions options) =>
        options.LifetimeMinutes is >= 1 and <= 30 &&
        options.MaximumFailedAttempts is >= 1 and <= 10
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "CustomerOtp lifetime must be 1-30 minutes and attempts must be 1-10.");
}

public sealed class CustomerRefreshTokenOptionsValidator :
    IValidateOptions<CustomerRefreshTokenOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        CustomerRefreshTokenOptions options) =>
        options.LifetimeDays is >= 1 and <= 365
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "CustomerRefreshTokens:LifetimeDays must be between 1 and 365.");
}

public sealed class CustomerSmtpOptionsValidator :
    IValidateOptions<CustomerSmtpOptions>
{
    public ValidateOptionsResult Validate(string? name, CustomerSmtpOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        return !string.IsNullOrWhiteSpace(options.Host) &&
               options.Port is >= 1 and <= 65535 &&
               !string.IsNullOrWhiteSpace(options.FromAddress)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "Enabled CustomerSmtp requires Host, a valid Port, and FromAddress.");
    }
}

public interface IOtpCodeGenerator
{
    string Generate();
}

public sealed class OtpCodeGenerator : IOtpCodeGenerator
{
    public string Generate() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}

public static class OtpCodeHasher
{
    public static byte[] CreateSalt() => RandomNumberGenerator.GetBytes(16);

    public static byte[] Hash(string code, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            code,
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);

    public static bool Matches(string code, byte[] salt, byte[] expectedHash) =>
        expectedHash.Length == 32 &&
        CryptographicOperations.FixedTimeEquals(Hash(code, salt), expectedHash);
}

public interface ICustomerOtpEmailSender
{
    Task SendAsync(string email, string code, CancellationToken cancellationToken);
}

public sealed class SmtpCustomerOtpEmailSender : ICustomerOtpEmailSender
{
    private readonly CustomerSmtpOptions _options;
    private readonly CustomerOtpOptions _otpOptions;

    public SmtpCustomerOtpEmailSender(
        IOptions<CustomerSmtpOptions> options,
        IOptions<CustomerOtpOptions> otpOptions)
    {
        _options = options.Value;
        _otpOptions = otpOptions.Value;
    }

    public async Task SendAsync(
        string email,
        string code,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled ||
            string.IsNullOrWhiteSpace(_options.Host) ||
            string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            throw new InvalidOperationException("Customer OTP email delivery is not configured.");
        }

        using var message = CreateMessage(email, code);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl
        };
        if (!string.IsNullOrWhiteSpace(_options.UserName))
        {
            client.Credentials = new NetworkCredential(
                _options.UserName,
                _options.Password);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }

    internal MailMessage CreateMessage(string email, string code)
    {
        var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = "رمز التحقق من غسيلي | Ghseeli verification code",
            Body =
                $"رمز التحقق الخاص بك هو {code}. تنتهي صلاحيته خلال {_otpOptions.LifetimeMinutes} دقائق." +
                Environment.NewLine +
                $"Your verification code is {code}. It expires in {_otpOptions.LifetimeMinutes} minutes.",
            IsBodyHtml = false
        };
        message.To.Add(email);
        return message;
    }
}

public sealed record IssuedRefreshToken(
    string Token,
    DateTimeOffset ExpiresAtUtc);

public sealed record RotatedRefreshToken(
    Guid UserId,
    string Email,
    string FullName,
    string AccessToken,
    string Token,
    DateTimeOffset ExpiresAtUtc);

public interface ICustomerRefreshTokenService
{
    Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken);
    Task<RotatedRefreshToken> RotateAsync(
        string token,
        Func<Guid, string, string, Task<string>> accessTokenFactory,
        CancellationToken cancellationToken);
}

public sealed class CustomerRefreshTokenException : Exception
{
    public CustomerRefreshTokenException(string code, int statusCode)
        : base("The refresh token is invalid or expired.")
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

public sealed class CustomerRefreshTokenService : ICustomerRefreshTokenService
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly CustomerRefreshTokenOptions _options;

    public CustomerRefreshTokenService(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        IOptions<CustomerRefreshTokenOptions> options)
    {
        _context = context;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    public async Task<IssuedRefreshToken> IssueAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var rawToken = GenerateToken();
        var record = new CustomerRefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = Guid.NewGuid(),
            TokenHash = Hash(rawToken),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_options.LifetimeDays)
        };
        _context.CustomerRefreshTokens.Add(record);
        await _context.SaveChangesAsync(cancellationToken);
        return new(rawToken, record.ExpiresAtUtc);
    }

    public async Task<RotatedRefreshToken> RotateAsync(
        string token,
        Func<Guid, string, string, Task<string>> accessTokenFactory,
        CancellationToken cancellationToken)
    {
        if (!IsValidToken(token))
        {
            throw Invalid();
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            () => RotateCoreAsync(token, accessTokenFactory, cancellationToken));
    }

    private async Task<RotatedRefreshToken> RotateCoreAsync(
        string token,
        Func<Guid, string, string, Task<string>> accessTokenFactory,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginSerializableAsync(cancellationToken);
        var hash = Hash(token);
        var record = await _context.CustomerRefreshTokens
            .Include(value => value.User)
            .SingleOrDefaultAsync(
                value => value.TokenHash.SequenceEqual(hash),
                cancellationToken);
        if (record is null)
        {
            throw Invalid();
        }

        var now = _timeProvider.GetUtcNow();
        if (record.UsedAtUtc is not null || record.RevokedAtUtc is not null)
        {
            await RevokeFamilyAsync(record.FamilyId, now, cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            throw Invalid();
        }

        if (record.ExpiresAtUtc <= now || !record.User.IsActive)
        {
            await RevokeFamilyAsync(record.FamilyId, now, cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            throw Invalid();
        }

        var rawReplacement = GenerateToken();
        var replacement = new CustomerRefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = record.UserId,
            FamilyId = record.FamilyId,
            TokenHash = Hash(rawReplacement),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(_options.LifetimeDays)
        };
        record.UsedAtUtc = now;
        record.ReplacedByTokenId = replacement.Id;
        _context.CustomerRefreshTokens.Add(replacement);
        var accessToken = await accessTokenFactory(
            record.UserId,
            record.User.Email!,
            record.User.FullName);
        await _context.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return new(
            record.UserId,
            record.User.Email!,
            record.User.FullName,
            accessToken,
            rawReplacement,
            replacement.ExpiresAtUtc);
    }

    private async Task RevokeFamilyAsync(
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var active = await _context.CustomerRefreshTokens
            .Where(value => value.FamilyId == familyId && value.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var value in active)
        {
            value.RevokedAtUtc = now;
        }
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginSerializableAsync(
        CancellationToken cancellationToken) =>
        _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;

    private static Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken) =>
        transaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

    private static string GenerateToken() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static bool IsValidToken(string? token)
    {
        if (token?.Length != 43)
        {
            return false;
        }
        try
        {
            return WebEncoders.Base64UrlDecode(token).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Hash(string token) =>
        SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(token));

    private static CustomerRefreshTokenException Invalid() =>
        new(CustomerAuthProblemCodes.RefreshTokenInvalid, StatusCodes.Status401Unauthorized);
}

public interface ICustomerOtpAuthenticationService
{
    Task RequestAsync(string email, CancellationToken cancellationToken);
    Task<AuthResponse> ConfirmAsync(
        string email,
        string code,
        CancellationToken cancellationToken);
}

public sealed class CustomerOtpException : Exception
{
    public CustomerOtpException(string code, int statusCode, string message)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

public sealed class CustomerOtpAuthenticationService : ICustomerOtpAuthenticationService
{
    private readonly ApplicationDbContext _context;
    private readonly Microsoft.AspNetCore.Identity.UserManager<User> _userManager;
    private readonly IAuthService _authService;
    private readonly ICustomerRefreshTokenService _refreshTokens;
    private readonly ICustomerOtpEmailSender _emailSender;
    private readonly IOtpCodeGenerator _codeGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly CustomerOtpOptions _options;
    private readonly IConfiguration _configuration;
    private readonly IAppLogger _logger;

    public CustomerOtpAuthenticationService(
        ApplicationDbContext context,
        Microsoft.AspNetCore.Identity.UserManager<User> userManager,
        IAuthService authService,
        ICustomerRefreshTokenService refreshTokens,
        ICustomerOtpEmailSender emailSender,
        IOtpCodeGenerator codeGenerator,
        TimeProvider timeProvider,
        IOptions<CustomerOtpOptions> options,
        IConfiguration configuration,
        IAppLogger logger)
    {
        _context = context;
        _userManager = userManager;
        _authService = authService;
        _refreshTokens = refreshTokens;
        _emailSender = emailSender;
        _codeGenerator = codeGenerator;
        _timeProvider = timeProvider;
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RequestAsync(string email, CancellationToken cancellationToken)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        var issued = await strategy.ExecuteAsync(
            () => CreateChallengeAsync(email, cancellationToken));

        try
        {
            await _emailSender.SendAsync(
                email.Trim(),
                issued.Code,
                cancellationToken);
            var activationStrategy = _context.Database.CreateExecutionStrategy();
            await activationStrategy.ExecuteAsync(
                () => ActivateChallengeAsync(issued.Challenge.Id, cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            try
            {
                var cleanupStrategy = _context.Database.CreateExecutionStrategy();
                await cleanupStrategy.ExecuteAsync(
                    () => ConsumeUndeliveredChallengeAsync(
                        issued.Challenge.Id,
                        CancellationToken.None));
            }
            catch (Exception cleanupException)
            {
                _logger.LogError(
                    $"OTP delivery cleanup failed: exceptionType={cleanupException.GetType().Name}.");
            }
            _logger.LogWarning(
                $"OTP delivery failed: exceptionType={exception.GetType().Name}.");
            throw new CustomerOtpException(
                CustomerAuthProblemCodes.OtpDeliveryUnavailable,
                StatusCodes.Status503ServiceUnavailable,
                "The verification code could not be delivered.");
        }
    }

    private async Task<(CustomerOtpChallenge Challenge, string Code)> CreateChallengeAsync(
        string email,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginSerializableAsync(cancellationToken);
        var normalizedEmail = _userManager.NormalizeEmail(email);
        var now = _timeProvider.GetUtcNow();
        var code = _codeGenerator.Generate();
        var salt = OtpCodeHasher.CreateSalt();
        var current = new CustomerOtpChallenge
        {
            Id = Guid.NewGuid(),
            NormalizedEmail = normalizedEmail,
            CodeSalt = salt,
            CodeHash = OtpCodeHasher.Hash(code, salt),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(_options.LifetimeMinutes)
        };
        _context.CustomerOtpChallenges.Add(current);
        await _context.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return (current, code);
    }

    private async Task ActivateChallengeAsync(
        Guid challengeId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginSerializableAsync(cancellationToken);
        var current = await _context.CustomerOtpChallenges
            .SingleAsync(value => value.Id == challengeId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var previous = await _context.CustomerOtpChallenges
            .Where(value =>
                value.NormalizedEmail == current.NormalizedEmail &&
                value.Id != challengeId &&
                value.DeliveredAtUtc != null &&
                value.ConsumedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var challenge in previous)
        {
            challenge.ConsumedAtUtc = now;
        }
        current.DeliveredAtUtc = now;
        await _context.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
    }

    private async Task ConsumeUndeliveredChallengeAsync(
        Guid challengeId,
        CancellationToken cancellationToken)
    {
        var challenge = await _context.CustomerOtpChallenges
            .SingleOrDefaultAsync(value => value.Id == challengeId, cancellationToken);
        if (challenge is null || challenge.DeliveredAtUtc is not null)
        {
            return;
        }

        challenge.ConsumedAtUtc = _timeProvider.GetUtcNow();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuthResponse> ConfirmAsync(
        string email,
        string code,
        CancellationToken cancellationToken)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            () => ConfirmCoreAsync(email, code, cancellationToken));
    }

    private async Task<AuthResponse> ConfirmCoreAsync(
        string email,
        string code,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginSerializableAsync(cancellationToken);
        var normalizedEmail = _userManager.NormalizeEmail(email);
        var challenge = await _context.CustomerOtpChallenges
            .Where(value =>
                value.NormalizedEmail == normalizedEmail &&
                value.DeliveredAtUtc != null &&
                value.ConsumedAtUtc == null)
            .OrderByDescending(value => value.DeliveredAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow();

        if (challenge is null ||
            challenge.ConsumedAtUtc is not null ||
            challenge.ExpiresAtUtc <= now)
        {
            throw InvalidOtp();
        }

        if (!OtpCodeHasher.Matches(code, challenge.CodeSalt, challenge.CodeHash))
        {
            challenge.FailedAttemptCount++;
            if (challenge.FailedAttemptCount >= _options.MaximumFailedAttempts)
            {
                challenge.ConsumedAtUtc = now;
            }
            await _context.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            throw challenge.FailedAttemptCount >= _options.MaximumFailedAttempts
                ? new CustomerOtpException(
                    CustomerAuthProblemCodes.OtpAttemptLimit,
                    StatusCodes.Status429TooManyRequests,
                    "Too many incorrect verification attempts.")
                : InvalidOtp();
        }

        challenge.ConsumedAtUtc = now;
        var user = await _userManager.FindByEmailAsync(email);
        var isNewUser = user is null;
        if (user is null)
        {
            user = new User
            {
                Id = Guid.NewGuid(),
                UserName = email.Trim(),
                Email = email.Trim(),
                EmailConfirmed = true,
                FullName = string.Empty,
                IsActive = true,
                CreatedAt = now.UtcDateTime
            };
            var created = await _userManager.CreateAsync(user);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException("Unable to create the OTP-authenticated user.");
            }
            var role = await _userManager.AddToRoleAsync(user, AppRoles.User);
            if (!role.Succeeded)
            {
                await _userManager.DeleteAsync(user);
                throw new InvalidOperationException("Unable to assign the customer role.");
            }
        }
        else if (!user.IsActive)
        {
            throw InvalidOtp();
        }
        else if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
        }

        isNewUser = isNewUser || string.IsNullOrWhiteSpace(user.FullName);

        await _context.SaveChangesAsync(cancellationToken);
        var accessToken = await _authService.GenerateJwtTokenAsync(
            user.Id,
            user.Email!,
            user.FullName);
        var refreshToken = await _refreshTokens.IssueAsync(user.Id, cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        var expirationMinutes = int.Parse(
            _configuration["JwtSettings:ExpirationMinutes"] ?? "60");
        return new AuthResponse
        {
            UserId = user.Id,
            Email = user.Email!,
            FullName = user.FullName,
            Token = accessToken,
            ExpiresAt = now.UtcDateTime.AddMinutes(expirationMinutes),
            RefreshToken = refreshToken.Token,
            RefreshTokenExpiresAt = refreshToken.ExpiresAtUtc,
            IsNewUser = isNewUser
        };
    }

    private async Task<IDbContextTransaction?> BeginSerializableAsync(
        CancellationToken cancellationToken) =>
        _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;

    private static Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken) =>
        transaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

    private static CustomerOtpException InvalidOtp() =>
        new(
            CustomerAuthProblemCodes.OtpInvalid,
            StatusCodes.Status401Unauthorized,
            "The verification code is invalid or expired.");
}
