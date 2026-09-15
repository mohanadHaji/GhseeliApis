using FluentAssertions;
using GhseeliApis.Models;
using GhseeliApis.Persistence;
using GhseeliApis.Services.Auth;
using GhseeliApis.Services.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Tests the customer OTP and refresh-token HTTP contract.
/// </summary>
public sealed class CustomerOtpAndRefreshApiIntegrationTests
{
    [Fact]
    [Trait("ScenarioId", "STEP23-OTP-REQUEST-001")]
    [Trait("ScenarioId", "STEP23-OTP-CONFIRM-006")]
    [Trait("ScenarioId", "STEP23-REFRESH-010")]
    [Trait("ScenarioId", "STEP23-REFRESH-012")]
    public async Task NewUserOtpAndRefresh_RotateTokensAndRejectReuse()
    {
        var sender = new RecordingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateApiClient();
        var email = $"otp-{Guid.NewGuid():N}@example.com";

        using var requestResponse = await client.PostAsJsonAsync(
            "/api/Auth/otp/request",
            new { email });
        var requestBody = await requestResponse.Content.ReadAsStringAsync();
        requestResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        requestBody.Should().NotContain("123456");
        sender.LastCode.Should().Be("123456");

        using var confirmResponse = await client.PostAsJsonAsync(
            "/api/Auth/otp/confirm",
            new { email, code = sender.LastCode });
        using var confirm = JsonDocument.Parse(
            await confirmResponse.Content.ReadAsStringAsync());
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.OK, confirm.RootElement.ToString());
        confirmResponse.Headers.CacheControl!.NoStore.Should().BeTrue();
        confirm.RootElement.GetProperty("isNewUser").GetBoolean().Should().BeTrue();
        var firstRefresh = confirm.RootElement.GetProperty("refreshToken").GetString()!;
        firstRefresh.Should().HaveLength(43);

        using var refreshResponse = await client.PostAsJsonAsync(
            "/api/Auth/refresh",
            new { refreshToken = firstRefresh });
        using var refresh = JsonDocument.Parse(
            await refreshResponse.Content.ReadAsStringAsync());
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replacement = refresh.RootElement.GetProperty("refreshToken").GetString();
        replacement.Should().HaveLength(43).And.NotBe(firstRefresh);

        using var reuseResponse = await client.PostAsJsonAsync(
            "/api/Auth/refresh",
            new { refreshToken = firstRefresh });
        using var reuse = JsonDocument.Parse(
            await reuseResponse.Content.ReadAsStringAsync());
        reuseResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        reuse.RootElement.GetProperty("code").GetString()
            .Should().Be(CustomerAuthProblemCodes.RefreshTokenInvalid);
    }

    [Fact]
    [Trait("ScenarioId", "STEP23-OTP-CONFIRM-007")]
    public async Task OtpCode_IsScopedToTheRequestedEmail()
    {
        var sender = new RecordingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateApiClient();

        await client.PostAsJsonAsync(
            "/api/Auth/otp/request",
            new { email = "first@example.com" });
        using var response = await client.PostAsJsonAsync(
            "/api/Auth/otp/confirm",
            new { email = "second@example.com", code = sender.LastCode });
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(
            HttpStatusCode.Unauthorized,
            problem.RootElement.ToString());
        problem.RootElement.GetProperty("code").GetString()
            .Should().Be(CustomerAuthProblemCodes.OtpInvalid);
    }

    [Fact]
    [Trait("ScenarioId", "STEP23-OTP-CONFIRM-005")]
    public async Task ExistingUserOtp_ReturnsIsNewUserFalse()
    {
        var sender = new RecordingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateApiClient();
        var email = $"existing-{Guid.NewGuid():N}@example.com";
        using var register = await client.PostAsJsonAsync(
            "/api/Auth/register",
            new
            {
                email,
                password = "Test123!",
                confirmPassword = "Test123!",
                fullName = "Existing User"
            });
        register.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var registrationBody = JsonDocument.Parse(
                   await register.Content.ReadAsStringAsync()))
        {
            registrationBody.RootElement.GetProperty("refreshToken").GetString()
                .Should().HaveLength(43);
            registrationBody.RootElement.GetProperty("refreshTokenExpiresAt")
                .GetDateTimeOffset().Should().BeAfter(factory.TimeProvider.GetUtcNow());
        }

        await client.PostAsJsonAsync("/api/Auth/otp/request", new { email });
        using var response = await client.PostAsJsonAsync(
            "/api/Auth/otp/confirm",
            new { email, code = sender.LastCode });
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.RootElement.GetProperty("isNewUser").GetBoolean().Should().BeFalse();
        body.RootElement.GetProperty("refreshToken").GetString()
            .Should().HaveLength(43);
    }

    [Fact]
    public async Task PasswordRegistration_RefreshFailure_RollsBackCreatedUser()
    {
        await using var factory = CreateFactory(
            new RecordingSender(),
            services =>
            {
                services.RemoveAll<ICustomerRefreshTokenService>();
                services.AddScoped<ICustomerRefreshTokenService, ThrowingRefreshTokenService>();
            });
        using var client = factory.CreateApiClient();
        var email = $"rollback-{Guid.NewGuid():N}@example.com";

        using var response = await client.PostAsJsonAsync(
            "/api/Auth/register",
            new
            {
                email,
                password = "Test123!",
                confirmPassword = "Test123!",
                fullName = "Rollback User"
            });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .Users.AnyAsync(value => value.Email == email))
            .Should().BeFalse();
    }

    [Fact]
    [Trait("ScenarioId", "STEP23-OTP-REQUEST-003")]
    public async Task ConcurrentOtpRequests_LastDeliveredCodeIsTheOnlyValidCode()
    {
        var sender = new OutOfOrderSender();
        await using var factory = CreateFactory(
            new RecordingSender(),
            services =>
            {
                services.RemoveAll<ICustomerOtpEmailSender>();
                services.RemoveAll<IOtpCodeGenerator>();
                services.AddSingleton<ICustomerOtpEmailSender>(sender);
                services.AddSingleton<IOtpCodeGenerator>(new SequentialOtpCodeGenerator());
            });
        using var client = factory.CreateApiClient();
        var email = $"concurrent-{Guid.NewGuid():N}@example.com";

        var firstRequest = client.PostAsJsonAsync(
            "/api/Auth/otp/request",
            new { email });
        await sender.FirstDeliveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var secondResponse = await client.PostAsJsonAsync(
            "/api/Auth/otp/request",
            new { email });
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        sender.ReleaseFirstDelivery.TrySetResult();
        using var firstResponse = await firstRequest;
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var invalidated = await client.PostAsJsonAsync(
            "/api/Auth/otp/confirm",
            new { email, code = "222222" });
        invalidated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var valid = await client.PostAsJsonAsync(
            "/api/Auth/otp/confirm",
            new { email, code = "111111" });
        valid.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("ScenarioId", "STEP23-OTP-REQUEST-004")]
    public async Task OtpDeliveryFailure_ReturnsServiceUnavailableAndConsumesCode()
    {
        await using var factory = CreateFactory(
            new RecordingSender(),
            services =>
            {
                services.RemoveAll<ICustomerOtpEmailSender>();
                services.AddSingleton<ICustomerOtpEmailSender>(
                    new FailingSender());
            });
        using var client = factory.CreateApiClient();
        var email = $"failed-{Guid.NewGuid():N}@example.com";

        using var response = await client.PostAsJsonAsync(
            "/api/Auth/otp/request",
            new { email });
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        problem.RootElement.GetProperty("code").GetString()
            .Should().Be(CustomerAuthProblemCodes.OtpDeliveryUnavailable);
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .CustomerOtpChallenges.SingleAsync())
            .ConsumedAtUtc.Should().NotBeNull();
    }

    [Fact]
    [Trait("ScenarioId", "STEP23-OTP-CONFIRM-008")]
    [Trait("ScenarioId", "STEP23-OTP-CONFIRM-009")]
    public async Task IncorrectOtp_IsBoundedAndThenPermanentlyRejected()
    {
        var sender = new RecordingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateApiClient();
        var email = $"attempts-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/Auth/otp/request", new { email });

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var invalid = await client.PostAsJsonAsync(
                "/api/Auth/otp/confirm",
                new { email, code = "000000" });
            invalid.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var limited = await client.PostAsJsonAsync(
            "/api/Auth/otp/confirm",
            new { email, code = "000000" });
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        using var validAfterLimit = await client.PostAsJsonAsync(
            "/api/Auth/otp/confirm",
            new { email, code = sender.LastCode });
        validAfterLimit.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("ScenarioId", "STEP23-OTP-REQUEST-002")]
    [Trait("ScenarioId", "STEP23-OTP-CONFIRM-008")]
    public async Task OtpEndpoints_RejectInvalidEmailAndExpiredCode()
    {
        var sender = new RecordingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateApiClient();
        using var invalidEmail = await client.PostAsJsonAsync(
            "/api/Auth/otp/request",
            new { email = "not-an-email" });
        invalidEmail.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var email = $"expired-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/Auth/otp/request", new { email });
        factory.TimeProvider.Advance(TimeSpan.FromMinutes(6));
        using var expired = await client.PostAsJsonAsync(
            "/api/Auth/otp/confirm",
            new { email, code = sender.LastCode });
        expired.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("ScenarioId", "STEP23-REFRESH-011")]
    public async Task UnknownRefreshToken_ReturnsStableUnauthorizedProblem()
    {
        await using var factory = CreateFactory(new RecordingSender());
        using var client = factory.CreateApiClient();
        var unknown = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            Enumerable.Repeat((byte)42, 32).ToArray());

        using var response = await client.PostAsJsonAsync(
            "/api/Auth/refresh",
            new { refreshToken = unknown });
        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        problem.RootElement.GetProperty("code").GetString()
            .Should().Be(CustomerAuthProblemCodes.RefreshTokenInvalid);
    }

    [Fact]
    [Trait("ScenarioId", "STEP23-REFRESH-013")]
    public async Task RefreshToken_ForInactiveUser_IsRejected()
    {
        await using var factory = CreateFactory(new RecordingSender());
        using var client = factory.CreateApiClient();
        var email = $"inactive-{Guid.NewGuid():N}@example.com";
        using var register = await client.PostAsJsonAsync(
            "/api/Auth/register",
            new
            {
                email,
                password = "Test123!",
                confirmPassword = "Test123!",
                fullName = "Inactive User"
            });
        using var registration = JsonDocument.Parse(
            await register.Content.ReadAsStringAsync());
        var refreshToken = registration.RootElement
            .GetProperty("refreshToken").GetString()!;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await context.Users.SingleAsync(value => value.Email == email);
            user.IsActive = false;
            await context.SaveChangesAsync();
        }

        using var response = await client.PostAsJsonAsync(
            "/api/Auth/refresh",
            new { refreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("ScenarioId", "STEP23-DEVICE-015")]
    [Trait("ScenarioId", "STEP23-DEVICE-016")]
    public async Task DeviceRegistration_PersistsOptionalFcmTokenWithoutEchoingIt()
    {
        await using var factory = CreateFactory(new RecordingSender());
        using var client = factory.CreateApiClient();
        var installationId = Guid.NewGuid();

        using var response = await client.PostAsJsonAsync(
            "/api/v1/devices/register",
            new
            {
                installationId,
                platform = "Android",
                appVersion = "1.0.0",
                fcmToken = "  fcm-value  "
            });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().NotContain("fcm-value");
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>()
            .CustomerDevices.SingleAsync(value =>
                value.InstallationId == installationId);
        stored.FcmToken.Should().Be("fcm-value");
    }

    [Fact]
    [Trait("ScenarioId", "STEP23-DEVICE-016")]
    [Trait("ScenarioId", "STEP23-DEVICE-017")]
    [Trait("ScenarioId", "STEP23-DEVICE-018")]
    public async Task DeviceRegistration_NormalizesRotatesAndValidatesFcmToken()
    {
        await using var factory = CreateFactory(new RecordingSender());
        using var client = factory.CreateApiClient();
        var installationId = Guid.NewGuid();
        using var issued = await client.PostAsJsonAsync(
            "/api/v1/devices/register",
            new
            {
                installationId,
                platform = "iOS",
                fcmToken = " "
            });
        using var issuedBody = JsonDocument.Parse(
            await issued.Content.ReadAsStringAsync());
        issued.StatusCode.Should().Be(HttpStatusCode.OK);
        var deviceToken = issuedBody.RootElement.GetProperty("token").GetString()!;
        using (var scope = factory.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .CustomerDevices.SingleAsync(value =>
                        value.InstallationId == installationId))
                .FcmToken.Should().BeNull();
        }

        using var rotateRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/devices/register")
        {
            Content = JsonContent.Create(new
            {
                installationId,
                platform = "Android",
                fcmToken = "replacement"
            })
        };
        rotateRequest.Headers.Add("X-Device-Token", deviceToken);
        using var rotated = await client.SendAsync(rotateRequest);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = factory.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                    .CustomerDevices.SingleAsync(value =>
                        value.InstallationId == installationId))
                .FcmToken.Should().Be("replacement");
        }

        using var oversized = await client.PostAsJsonAsync(
            "/api/v1/devices/register",
            new
            {
                installationId = Guid.NewGuid(),
                platform = "Android",
                fcmToken = new string('x', 4097)
            });
        oversized.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("ScenarioId", "STEP24-DEVICE-LEGACY-003")]
    [Trait("ScenarioId", "STEP24-DEVICE-CROSS-005")]
    [Trait("ScenarioId", "STEP24-DEVICE-OLD-TOKEN-009")]
    public async Task AuthenticatedCustomer_RecoversLegacyDevice_AndOtherCustomerCannotTakeIt()
    {
        var sender = new RecordingSender();
        await using var factory = CreateFactory(sender);
        using var client = factory.CreateApiClient();
        var installationId = Guid.NewGuid();

        using var anonymousRegistration = await client.PostAsJsonAsync(
            "/api/v1/devices/register",
            new { installationId, platform = "Android" });
        var anonymousContent = await anonymousRegistration.Content.ReadAsStringAsync();
        anonymousRegistration.StatusCode.Should().Be(
            HttpStatusCode.OK,
            anonymousContent);
        using var anonymousBody = JsonDocument.Parse(anonymousContent);
        var oldDeviceToken = anonymousBody.RootElement.GetProperty("token").GetString()!;

        var owner = await AuthenticateNewOtpUserAsync(
            client,
            sender,
            $"owner-{Guid.NewGuid():N}@example.test");
        using var recoveryRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/devices/register")
        {
            Content = JsonContent.Create(new
            {
                installationId,
                platform = "iOS",
                appVersion = "2.0.0",
                fcmToken = "replacement-fcm"
            })
        };
        recoveryRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", owner.Token);
        using var recovered = await client.SendAsync(recoveryRequest);

        recovered.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = factory.Services.CreateScope())
        {
            var stored = await scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>()
                .CustomerDevices.SingleAsync(device =>
                    device.InstallationId == installationId);
            stored.UserId.Should().Be(owner.UserId);
            stored.FcmToken.Should().Be("replacement-fcm");
        }

        var other = await AuthenticateNewOtpUserAsync(
            client,
            sender,
            $"other-{Guid.NewGuid():N}@example.test");
        using var crossOwnerRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/devices/register")
        {
            Content = JsonContent.Create(new { installationId, platform = "Android" })
        };
        crossOwnerRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", other.Token);
        using var crossOwner = await client.SendAsync(crossOwnerRequest);
        using var crossOwnerProblem = JsonDocument.Parse(
            await crossOwner.Content.ReadAsStringAsync());

        crossOwner.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        crossOwnerProblem.RootElement.GetProperty("code").GetString()
            .Should().Be(DeviceProblemCodes.OwnerConflict);

        using var oldTokenRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/devices/register")
        {
            Content = JsonContent.Create(new { installationId, platform = "Android" })
        };
        oldTokenRequest.Headers.Add("X-Device-Token", oldDeviceToken);
        using var oldTokenResponse = await client.SendAsync(oldTokenRequest);
        oldTokenResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("ScenarioId", "STEP23-SWAGGER-019")]
    public async Task Swagger_PublishesNewAuthRoutesAndChangedFields()
    {
        await using var factory = CreateFactory(new RecordingSender());
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var paths = document.RootElement.GetProperty("paths");
        paths.TryGetProperty("/api/Auth/otp/request", out _).Should().BeTrue();
        paths.TryGetProperty("/api/Auth/otp/confirm", out _).Should().BeTrue();
        paths.TryGetProperty("/api/Auth/refresh", out _).Should().BeTrue();
        var deviceRegistration = paths
            .GetProperty("/api/v1/devices/register")
            .GetProperty("post");
        deviceRegistration.GetProperty("responses")
            .TryGetProperty("403", out _).Should().BeTrue();
        deviceRegistration.GetProperty("description").GetString()
            .Should().Contain("optional Customer Bearer token");
        deviceRegistration.GetProperty("security").GetArrayLength()
            .Should().Be(0);
        deviceRegistration.GetProperty("parameters").EnumerateArray()
            .Should().Contain(parameter =>
                parameter.GetProperty("name").GetString() == "X-Device-Token" &&
                parameter.GetProperty("required").GetBoolean() == false);
        var schemas = document.RootElement.GetProperty("components")
            .GetProperty("schemas");
        schemas.GetProperty("RegisterDeviceRequest").GetProperty("properties")
            .TryGetProperty("fcmToken", out _).Should().BeTrue();
        var bookingProperties = schemas.GetProperty("ConfirmedBookingResponse")
            .GetProperty("properties");
        bookingProperties.TryGetProperty("referenceId", out _).Should().BeTrue();
        bookingProperties.TryGetProperty("reference", out _).Should().BeFalse();
    }

    private static CheckoutDraftApiFactory CreateFactory(
        RecordingSender sender,
        Action<IServiceCollection>? configure = null) =>
        new(
            configureTestServices: services =>
            {
                services.RemoveAll<ICustomerOtpEmailSender>();
                services.RemoveAll<IOtpCodeGenerator>();
                services.AddSingleton<ICustomerOtpEmailSender>(sender);
                services.AddSingleton<IOtpCodeGenerator>(
                    new FixedOtpCodeGenerator());
                configure?.Invoke(services);
            });

    private static async Task<(Guid UserId, string Token)> AuthenticateNewOtpUserAsync(
        HttpClient client,
        RecordingSender sender,
        string email)
    {
        using var request = await client.PostAsJsonAsync(
            "/api/Auth/otp/request",
            new { email });
        request.EnsureSuccessStatusCode();
        using var confirmation = await client.PostAsJsonAsync(
            "/api/Auth/otp/confirm",
            new { email, code = sender.LastCode });
        confirmation.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(
            await confirmation.Content.ReadAsStringAsync());
        return (
            body.RootElement.GetProperty("userId").GetGuid(),
            body.RootElement.GetProperty("token").GetString()!);
    }

    private sealed class FixedOtpCodeGenerator : IOtpCodeGenerator
    {
        public string Generate() => "123456";
    }

    private sealed class RecordingSender : ICustomerOtpEmailSender
    {
        public string LastCode { get; private set; } = string.Empty;

        public Task SendAsync(
            string email,
            string code,
            CancellationToken cancellationToken)
        {
            LastCode = code;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingSender : ICustomerOtpEmailSender
    {
        public Task SendAsync(
            string email,
            string code,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("SMTP unavailable");
    }

    private sealed class SequentialOtpCodeGenerator : IOtpCodeGenerator
    {
        private int _count;

        public string Generate() =>
            Interlocked.Increment(ref _count) == 1 ? "111111" : "222222";
    }

    private sealed class OutOfOrderSender : ICustomerOtpEmailSender
    {
        public TaskCompletionSource FirstDeliveryStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstDelivery { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SendAsync(
            string email,
            string code,
            CancellationToken cancellationToken)
        {
            if (code != "111111")
            {
                return;
            }

            FirstDeliveryStarted.TrySetResult();
            await ReleaseFirstDelivery.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ThrowingRefreshTokenService : ICustomerRefreshTokenService
    {
        public Task<IssuedRefreshToken> IssueAsync(
            Guid userId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Refresh persistence unavailable");

        public Task<RotatedRefreshToken> RotateAsync(
            string token,
            Func<Guid, string, string, Task<string>> accessTokenFactory,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
