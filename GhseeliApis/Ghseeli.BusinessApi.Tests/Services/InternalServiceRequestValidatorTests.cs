using FluentAssertions;
using Ghseeli.BusinessApi.InternalServices;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Ghseeli.BusinessApi.Tests.Services;

/// <summary>
/// Verifies internal request validation security logging and secret rotation behavior.
/// </summary>
public class InternalServiceRequestValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WhenSignatureIsInvalid_DoesNotLogSecretSignatureOrBody()
    {
        const string secret = "ValidatorTestSecret_Minimum32Characters__";
        const string sensitiveBody = "{\"sensitive\":\"body-should-not-log\"}";
        const string providedSignature = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var logger = new RecordingLogger();
        var validator = CreateValidator(logger);
        var context = CreateContext(
            secret,
            sensitiveBody,
            providedSignature,
            useNextSecret: false);

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        result.IsAuthenticated.Should().BeFalse();
        logger.Messages.Should().ContainSingle();
        logger.Messages.Single().Should().NotContain(secret);
        logger.Messages.Single().Should().NotContain(providedSignature);
        logger.Messages.Single().Should().NotContain("body-should-not-log");
    }

    [Fact]
    public async Task ValidateAsync_WhenNextSecretMatches_AcceptsRotatedSecret()
    {
        var logger = new RecordingLogger();
        var validator = CreateValidator(logger);
        var context = CreateContext(
            "ValidatorTestNextSecret_Minimum32Characters",
            "{\"valid\":true}",
            signature: null,
            useNextSecret: true);

        var result = await validator.ValidateAsync(context, CancellationToken.None);

        result.IsAuthenticated.Should().BeTrue();
        logger.Messages.Should().BeEmpty();
    }

    private static InternalServiceRequestValidator CreateValidator(RecordingLogger logger)
    {
        return new InternalServiceRequestValidator(
            new PassThroughNonceStore(),
            new TestClock(new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc)),
            Options.Create(new InternalServiceAuthenticationOptions
            {
                RequireHttps = true,
                AllowInsecureHttpInDevelopment = false,
                AllowedClockSkewSeconds = 120,
                NonceLifetimeSeconds = 300,
                Services =
                [
                    new InternalServiceDefinition
                    {
                        ServiceId = "customer-api-tests",
                        ActiveSecret = "ValidatorTestActiveSecret_Minimum32Chars",
                        NextSecret = "ValidatorTestNextSecret_Minimum32Characters",
                        AllowedOperations =
                        [
                            InternalServiceOperationNames.CatalogSnapshot,
                            InternalServiceOperationNames.AppointmentValidate
                        ]
                    }
                ]
            }),
            logger);
    }

    private static DefaultHttpContext CreateContext(
        string signingSecret,
        string body,
        string? signature,
        bool useNextSecret)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Method = HttpMethod.Post.Method;
        context.Request.Path = "/api/v1/internal/appointments/validate";
        context.Request.QueryString = new QueryString("?companyId=11111111-1111-1111-1111-111111111111");
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = Encoding.UTF8.GetByteCount(body);
        context.Request.ContentType = "application/json";
        context.Request.Headers[InternalServiceWireConstants.ServiceIdHeaderName] = "customer-api-tests";
        context.Request.Headers[InternalServiceWireConstants.TimestampHeaderName] =
            new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc).ToString("O");
        context.Request.Headers[InternalServiceWireConstants.NonceHeaderName] =
            "1234567890abcdef1234567890abcdef";

        var canonical = InternalServiceCanonicalRequest.Build(
            "customer-api-tests",
            HttpMethod.Post.Method,
            context.Request.Path.ToUriComponent(),
            [new KeyValuePair<string, string?>("companyId", "11111111-1111-1111-1111-111111111111")],
            context.Request.Headers[InternalServiceWireConstants.TimestampHeaderName].ToString(),
            context.Request.Headers[InternalServiceWireConstants.NonceHeaderName].ToString(),
            InternalServiceCanonicalRequest.ComputeSha256Hex(Encoding.UTF8.GetBytes(body)));
        var effectiveSignature = signature ?? Convert.ToHexString(
            new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret))
                .ComputeHash(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        context.Request.Headers[InternalServiceWireConstants.SignatureHeaderName] = effectiveSignature;

        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment
        {
            EnvironmentName = Environments.Production
        });
        context.RequestServices = services.BuildServiceProvider();

        return context;
    }

    private sealed class PassThroughNonceStore : IInternalServiceNonceStore
    {
        public Task<bool> TryAcceptAsync(
            string serviceId,
            string nonce,
            DateTime receivedAtUtc,
            DateTime expiresAtUtc,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class TestClock : ISystemClock
    {
        public TestClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Messages { get; } = [];

        public void LogInfo(string message) => Messages.Add(message);
        public void LogWarning(string message) => Messages.Add(message);
        public void LogError(string message) => Messages.Add(message);
        public void LogError(string message, Exception exception) => Messages.Add(message);
    }

    private sealed class TestHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Ghseeli.BusinessApi.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
