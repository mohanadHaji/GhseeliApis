using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ghseeli.Common.Logging;
using GhseeliApis.DTOs.Payment;
using GhseeliApis.Services.Payments;
using GhseeliApis.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;

namespace GhseeliApis.Tests.Integration;

/// <summary>
/// Exercises payment request limits through the real TestServer pipeline.
/// </summary>
public sealed class CustomerPaymentApiIntegrationTests
{
    private const string JwtSecret = "CheckoutDraftApiTestsSecret_Minimum32Chars";

    [Fact]
    public async Task LahzaEndpoints_InProductionByDefault_AreNotMappedOrDocumented()
    {
        var paymentId = Guid.NewGuid();
        var service = new ControlledPaymentService
        {
            Get = _ => CreatePaymentResponse(paymentId)
        };
        var token = CatalogTestSupport.CreateToken(90);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(
            service,
            [device],
            environmentName: "Production");
        using var client = factory.CreateApiClient();

        using var swaggerResponse = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(
            await swaggerResponse.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        swaggerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        paths.TryGetProperty("/api/v1/payments/intents", out _).Should().BeFalse();
        paths.TryGetProperty("/api/v1/payments/{id}/verify", out _).Should().BeFalse();
        paths.TryGetProperty("/api/lahza/webhook", out _).Should().BeFalse();
        paths.TryGetProperty("/api/v1/payments/{id}", out _).Should().BeTrue();

        foreach (var path in new[]
                 {
                     "/api/v1/payments/intents",
                     "/api/v1/payments/intents/",
                     $"/api/v1/payments/{Guid.NewGuid():D}/verify",
                     $"/api/v1/payments/{Guid.NewGuid():D}/verify/",
                     "/api/lahza/webhook",
                     "/api/lahza/webhook/"
                 })
        {
            using var response = await client.PostAsync(
                path,
                new StringContent("{}", Encoding.UTF8, "application/json"));
            await AssertPaymentProblemAsync(
                response,
                HttpStatusCode.NotFound,
                "resource_not_found",
                "ar");
        }

        using var readRequest = CreateAuthenticatedPaymentRequest(
            HttpMethod.Get,
            $"/api/v1/payments/{paymentId:D}",
            token);
        using var readResponse = await client.SendAsync(readRequest);
        readResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        service.GetCalls.Should().Be(1);
    }

    [Fact]
    public async Task LahzaVerification_InDevelopment_RemainsMappedAndReachable()
    {
        var paymentId = Guid.NewGuid();
        var service = new ControlledPaymentService
        {
            Get = _ => CreatePaymentResponse(paymentId)
        };
        var token = CatalogTestSupport.CreateToken(91);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(service, [device]);
        using var client = factory.CreateApiClient();
        using var request = CreateAuthenticatedPaymentRequest(
            HttpMethod.Post,
            $"/api/v1/payments/{paymentId:D}/verify",
            token);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        service.GetCalls.Should().Be(1);
    }

    [Fact]
    public async Task Swagger_DocumentsPaymentSchemasSecurityAndNoLegacyWrites()
    {
        await using var factory = new CheckoutDraftApiFactory();
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var create = paths.GetProperty("/api/v1/payments/intents").GetProperty("post");
        var read = paths.GetProperty("/api/v1/payments/{id}").GetProperty("get");
        var webhook = paths.GetProperty("/api/lahza/webhook").GetProperty("post");
        var requestSchema = root.GetProperty("components").GetProperty("schemas")
            .GetProperty(nameof(CreateCustomerPaymentIntentRequest))
            .GetProperty("properties");

        requestSchema.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("bookingId", "method");
        requestSchema.TryGetProperty("amount", out _).Should().BeFalse();
        requestSchema.TryGetProperty("currency", out _).Should().BeFalse();
        requestSchema.TryGetProperty("paymentMethodId", out _).Should().BeFalse();
        AssertBearerSecurity(create);
        AssertBearerSecurity(read);
        webhook.GetProperty("security")[0]
            .TryGetProperty("LahzaSignature", out var lahzaScopes).Should().BeTrue();
        lahzaScopes.GetArrayLength().Should().Be(0);

        paths.EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/payments", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty();
        paths.TryGetProperty("/api/v1/payments/intents", out var intentPath).Should().BeTrue();
        intentPath.TryGetProperty("put", out _).Should().BeFalse();
        intentPath.TryGetProperty("patch", out _).Should().BeFalse();
        intentPath.TryGetProperty("delete", out _).Should().BeFalse();
        root.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty("CustomerBearer").GetProperty("scheme").GetString().Should().Be("bearer");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostIntent_Over65536Bytes_ReturnsLocalizedPaymentProblem(bool chunked)
    {
        var token = CatalogTestSupport.CreateToken(91);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = new CheckoutDraftApiFactory(devices: [device]);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/payments/intents?language=he");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));
        request.Headers.Add("X-Device-Token", token);
        var bytes = Encoding.UTF8.GetBytes(new string('x', 65_537));
        request.Content = chunked
            ? new UnknownLengthContent(bytes)
            : new ByteArrayContent(bytes);
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");

        using var response = await client.SendAsync(request);
        await AssertPaymentProblemAsync(
            response,
            HttpStatusCode.RequestEntityTooLarge,
            CustomerPaymentErrorCodes.RequestTooLarge,
            "he");
    }

    [Fact]
    public async Task PostIntent_WrongContentType_UsesPaymentSpecificCode()
    {
        using var response = await SendAsync(
            """{"bookingId":"11111111-1111-1111-1111-111111111111","method":"Card"}""",
            "text/plain",
            "valid-key");
        await AssertPaymentProblemAsync(
            response,
            HttpStatusCode.UnsupportedMediaType,
            CustomerPaymentErrorCodes.PaymentUnsupportedMediaType,
            "ar");
    }

    [Theory]
    [InlineData("""{"bookingId":""", "valid-key", "payment_request_invalid")]
    [InlineData("""{"bookingId":"11111111-1111-1111-1111-111111111111","method":"Card"}""",
        null, "idempotency_key_required")]
    [InlineData("""{"bookingId":"11111111-1111-1111-1111-111111111111","method":"Card"}""",
        " ", "idempotency_key_invalid")]
    [InlineData("""{"bookingId":"11111111-1111-1111-1111-111111111111","method":""}""",
        "valid-key", "payment_request_invalid")]
    [InlineData("""{"bookingId":"11111111-1111-1111-1111-111111111111","method":"Wallet"}""",
        "valid-key", "payment_method_not_yet_supported")]
    public async Task PostIntent_UsesFrozenValidationCodes(
        string body,
        string? idempotencyKey,
        string expectedCode)
    {
        using var response = await SendAsync(body, "application/json", idempotencyKey);
        var expectedStatus = expectedCode == "payment_method_not_yet_supported"
            ? HttpStatusCode.Conflict
            : HttpStatusCode.BadRequest;
        await AssertPaymentProblemAsync(response, expectedStatus, expectedCode, "ar");
    }

    [Fact]
    public async Task PostIntent_ModelBindingProblem_ContainsSafeFieldErrors()
    {
        using var response = await SendAsync(
            """{"bookingId":""",
            "application/json",
            "valid-key");
        var root = await AssertPaymentProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            CustomerPaymentErrorCodes.Invalid,
            "ar");
        var fieldErrors = root.GetProperty("fieldErrors");
        fieldErrors.ValueKind.Should().Be(JsonValueKind.Object);
        fieldErrors.EnumerateObject().Should().NotBeEmpty();
        fieldErrors.ToString().Should().NotContain("JsonException");
        fieldErrors.ToString().Should().NotContain("System.");
    }

    [Fact]
    public async Task PostIntent_MissingBody_ReturnsInvalidRequestProblem()
    {
        var token = CatalogTestSupport.CreateToken(91);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = new CheckoutDraftApiFactory(devices: [device]);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/payments/intents");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));
        request.Headers.Add("X-Device-Token", token);
        request.Headers.Add("Idempotency-Key", "valid-key");

        using var response = await client.SendAsync(request);
        await AssertPaymentProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            CustomerPaymentErrorCodes.Invalid,
            "ar");
    }

    [Fact]
    public async Task PostIntent_NullBody_ReturnsLocalizedInvalidRequestProblem()
    {
        using var response = await SendAsync("null", "application/json", "valid-key");
        var root = await AssertPaymentProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            CustomerPaymentErrorCodes.Invalid,
            "ar");
        root.GetProperty("title").GetString()
            .Should().Be("تعذر إكمال طلب الدفع.");
        root.GetProperty("detail").GetString()
            .Should().Be("تعذر إكمال طلب الدفع.");
    }

    [Fact]
    public async Task PostIntent_MissingJwt_ReturnsTypedAuthenticationProblem()
    {
        await AssertAuthenticationProblemAsync(null);
    }

    [Fact]
    public async Task PostIntent_EmptyJwt_ReturnsTypedAuthenticationProblem()
    {
        await AssertAuthenticationProblemAsync("Bearer");
    }

    [Fact]
    public async Task PostIntent_WrongRole_ReturnsTypedAuthorizationProblem()
    {
        var token = CatalogTestSupport.CreateToken(91);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = new CheckoutDraftApiFactory(devices: [device]);
        using var client = factory.CreateApiClient();
        using var request = CreateValidRequest(token);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid(), "Admin"));

        using var response = await client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        await AssertPaymentProblemAsync(
            response,
            HttpStatusCode.Forbidden,
            "customer_authorization_forbidden",
            "ar");
    }

    [Theory]
    [InlineData("bad language")]
    [InlineData("en")]
    public async Task PostIntent_InvalidExplicitLanguage_ReturnsStableProblem(string language)
    {
        var token = CatalogTestSupport.CreateToken(91);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = new CheckoutDraftApiFactory(devices: [device]);
        using var client = factory.CreateApiClient();
        using var request = CreateValidRequest(
            token,
            $"?language={Uri.EscapeDataString(language)}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));

        using var response = await client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be("language_invalid");
        document.RootElement.GetProperty("type").GetString()
            .Should().Be("https://api.ghseeli.example/errors/language_invalid");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unsafe\r\ninjected-header: value")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task PostIntent_UnsafeCorrelationIsReplacedWithoutProviderOrLogDisclosure(
        string? unsafeCorrelation)
    {
        var service = new ControlledPaymentService();
        var logger = new FullExceptionCapturingLogger();
        var token = CatalogTestSupport.CreateToken(101);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(service, [device], logger);
        using var client = factory.CreateApiClient();
        using var request = CreateValidRequest(token);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));
        request.Content = new StringContent(
            """{"bookingId":"11111111-1111-1111-1111-111111111111","method":"Wallet"}""",
            Encoding.UTF8,
            "application/json");
        if (unsafeCorrelation is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", unsafeCorrelation);
        }

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var replacement = document.RootElement.GetProperty("correlationId").GetString();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        replacement.Should().MatchRegex("^[0-9a-f]{32}$");
        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle(replacement!);
        if (!string.IsNullOrEmpty(unsafeCorrelation))
        {
            replacement.Should().NotBe(unsafeCorrelation);
            body.Should().NotContain(unsafeCorrelation);
            logger.AllText.Should().NotContain(unsafeCorrelation);
        }
        service.CreateCalls.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-TRANSPORT-DUPLICATE-IDEMPOTENCY-128C")]
    [Trait("ScenarioId", "STEP15-INVARIANT-PAYMENT-159")]
    public async Task PostIntent_RepeatedIdempotencyHeader_ReturnsInvalidKeyProblem()
    {
        var token = CatalogTestSupport.CreateToken(91);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = new CheckoutDraftApiFactory(devices: [device]);
        using var client = factory.CreateApiClient();
        using var request = CreateValidRequest(token);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));
        request.Headers.Remove("Idempotency-Key");
        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            new[] { "first-key", "second-key" });

        using var response = await client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("code").GetString()
            .Should().Be(CustomerPaymentErrorCodes.IdempotencyKeyInvalid);
    }

    [Theory]
    [InlineData(null, "device_token_missing")]
    [InlineData("", "device_token_missing")]
    [InlineData("illegal token", "device_token_invalid")]
    public async Task PaymentDeviceFailures_ReturnSafeNoStoreProblem(
        string? deviceToken,
        string expectedCode)
    {
        var service = new ControlledPaymentService();
        await using var factory = CreateFactory(service);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/payments/{Guid.NewGuid():D}?language=he");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));
        if (deviceToken is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Device-Token", deviceToken);
        }

        using var response = await client.SendAsync(request);

        await AssertPaymentProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            expectedCode,
            "he");
        service.GetCalls.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-CUSTOMER-PAYMENT-INTENT-071")]
    [Trait("ScenarioId", "STEP15-INVARIANT-PAYMENT-159")]
    public async Task PostIntent_SuccessSerializesOnlyFrozenPublicContractAndIgnoresUnknownAuthorityFields()
    {
        var bookingId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 8, 24, 9, 30, 0, TimeSpan.Zero);
        var service = new ControlledPaymentService
        {
            Create = request => new CustomerPaymentResponse
            {
                Id = paymentId,
                BookingId = request.BookingId,
                Amount = 12.34m,
                Currency = "ILS",
                Method = "Card",
                Status = "Pending",
                ProviderStatus = "requires_confirmation",
                Provider = PaymentProviders.Lahza,
                ProviderReference = "GHSEELI-SAFE",
                CheckoutUrl = "https://checkout.lahza.test/pay/GHSEELI-SAFE",
                CreatedAtUtc = createdAt
            }
        };
        var token = CatalogTestSupport.CreateToken(91);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(service, [device]);
        using var client = factory.CreateApiClient();
        using var request = CreateValidRequest(token);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));
        request.Headers.Add("X-Correlation-Id", "step14-safe-correlation");
        request.Content = new StringContent(
            $$"""{"bookingId":"{{bookingId:D}}","method":"cArD","amount":0,"currency":"USD","userId":"{{Guid.NewGuid():D}}","status":"Completed","paymentMethodId":"pm_attacker"}""",
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Correlation-Id")
            .Should().ContainSingle("step14-safe-correlation");
        document.RootElement.EnumerateObject().Select(value => value.Name).Should().BeEquivalentTo(
            "id", "bookingId", "amount", "currency", "method", "status",
            "provider", "providerStatus", "providerReference", "checkoutUrl", "createdAtUtc");
        document.RootElement.GetProperty("id").GetGuid().Should().Be(paymentId);
        document.RootElement.GetProperty("amount").GetDecimal().Should().Be(12.34m);
        document.RootElement.GetProperty("currency").GetString().Should().Be("ILS");
        document.RootElement.GetProperty("method").GetString().Should().Be("Card");
        body.Should().NotContain("pm_attacker")
            .And.NotContain("userId")
            .And.NotContain("paymentIntentId")
            .And.NotContain("lahza_secret_")
            .And.NotContain("sk_test_");
        service.LastRequest!.BookingId.Should().Be(bookingId);
        service.LastRequest.Method.Should().Be("cArD");
    }

    [Theory]
    [Trait("ScenarioId", "STEP15-CUSTOMER-PAYMENT-READ-072")]
    [InlineData("Pending", true)]
    [InlineData("Completed", false)]
    [InlineData("Failed", false)]
    [InlineData("Refunded", false)]
    public async Task GetOwnedPayment_SerializesLifecycleSafeConfirmation(
        string status,
        bool confirmationExpected)
    {
        var paymentId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var service = new ControlledPaymentService
        {
            Get = _ => new CustomerPaymentResponse
            {
                Id = paymentId,
                BookingId = bookingId,
                Amount = 20m,
                Currency = "ILS",
                Method = "Card",
                Status = status,
                ProviderStatus = status == "Pending" ? "requires_confirmation" : null,
                Provider = PaymentProviders.Lahza,
                ProviderReference = "GHSEELI-SAFE",
                CheckoutUrl = confirmationExpected
                    ? "https://checkout.lahza.test/pay/GHSEELI-SAFE"
                    : null,
                CreatedAtUtc = DateTimeOffset.UtcNow
            }
        };
        var token = CatalogTestSupport.CreateToken(92);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(service, [device]);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/payments/{paymentId:D}?language=ar");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));
        request.Headers.Add("X-Device-Token", token);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        document.RootElement.GetProperty("status").GetString().Should().Be(status);
        document.RootElement.GetProperty("method").GetString().Should().Be("Card");
        if (confirmationExpected)
        {
            document.RootElement.GetProperty("checkoutUrl").GetString()
                .Should().StartWith("https://checkout.lahza.test/");
        }
        else
        {
            document.RootElement.GetProperty("checkoutUrl").ValueKind.Should().Be(JsonValueKind.Null);
        }
        document.RootElement.GetProperty("provider").GetString().Should().Be(PaymentProviders.Lahza);
        document.RootElement.GetProperty("providerReference").GetString()
            .Should().Be("GHSEELI-SAFE");
        body.Should().NotContain("providerTransactionId")
            .And.NotContain("userId")
            .And.NotContain("ownerDeviceId");
    }

    [Fact]
    public async Task GetPayment_MalformedIdIsBareRoute404AndDoesNotCallService()
    {
        var service = new ControlledPaymentService();
        var token = CatalogTestSupport.CreateToken(93);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(service, [device]);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/payments/not-a-guid?language=he");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));
        request.Headers.Add("X-Device-Token", token);

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        service.GetCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetPayment_MalformedOrExpiredJwtIsStableAuthenticationProblem(
        bool expired)
    {
        var service = new ControlledPaymentService();
        var token = CatalogTestSupport.CreateToken(96);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(service, [device]);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/payments/{Guid.NewGuid():D}?language=he");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            expired
                ? CreateJwt(Guid.NewGuid(), expires: DateTime.UtcNow.AddMinutes(-10))
                : "malformed.jwt");
        request.Headers.Add("X-Device-Token", token);

        using var response = await client.SendAsync(request);

        await AssertPaymentProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "customer_authentication_required",
            "he");
        service.GetCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetPayment_WrongRoleIsStableAuthorizationProblemWithoutDisclosure()
    {
        var service = new ControlledPaymentService();
        var token = CatalogTestSupport.CreateToken(97);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(service, [device]);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/payments/{Guid.NewGuid():D}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid(), "Admin"));
        request.Headers.Add("X-Device-Token", token);

        using var response = await client.SendAsync(request);

        await AssertPaymentProblemAsync(
            response,
            HttpStatusCode.Forbidden,
            "customer_authorization_forbidden",
            "ar");
        service.GetCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetPayment_ExpiredOrRotatedDeviceIsStableAndDoesNotCallService()
    {
        var service = new ControlledPaymentService();
        var expiredToken = CatalogTestSupport.CreateToken(98);
        var expiredDevice = CatalogTestSupport.CreateDevice(
            expiredToken,
            new DateTimeOffset(2026, 8, 23, 7, 0, 0, TimeSpan.Zero));
        await using var factory = CreateFactory(service, [expiredDevice]);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/payments/{Guid.NewGuid():D}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));
        request.Headers.Add("X-Device-Token", expiredToken);

        using var response = await client.SendAsync(request);

        await AssertPaymentProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "device_token_expired",
            "ar");
        service.GetCalls.Should().Be(0);
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-INVARIANT-PAYMENT-159")]
    public async Task GetPayment_WrongOwnerUnknownAndWrongDeviceShareNotFoundContract()
    {
        var service = new ControlledPaymentService { Get = _ => null };
        var token = CatalogTestSupport.CreateToken(99);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(service, [device]);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/payments/{Guid.NewGuid():D}?language=he");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));
        request.Headers.Add("X-Device-Token", token);

        using var response = await client.SendAsync(request);

        await AssertPaymentProblemAsync(
            response,
            HttpStatusCode.NotFound,
            CustomerPaymentErrorCodes.NotFound,
            "he");
        service.GetCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(409, CustomerPaymentErrorCodes.IdempotencyConflict)]
    [InlineData(409, CustomerPaymentErrorCodes.Ineligible)]
    [InlineData(409, CustomerPaymentErrorCodes.CurrencyUnsupported)]
    [InlineData(409, CustomerPaymentErrorCodes.AlreadyExists)]
    [InlineData(502, CustomerPaymentErrorCodes.GatewayAmbiguous)]
    [InlineData(503, CustomerPaymentErrorCodes.ProviderUnavailable)]
    public async Task PostIntent_ServiceFailuresMapToSafeExactHttpProblems(
        int status,
        string code)
    {
        var service = new ControlledPaymentService
        {
            Create = _ => throw new CustomerPaymentException(
                status,
                code,
                "sk_test_leak lahza_secret_leak customer@example.com")
        };
        var token = CatalogTestSupport.CreateToken(94);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(service, [device]);
        using var client = factory.CreateApiClient();
        using var request = CreateValidRequest(token);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));

        using var response = await client.SendAsync(request);

        var root = await AssertPaymentProblemAsync(
            response,
            (HttpStatusCode)status,
            code,
            "ar");
        root.ToString().Should().NotContain("customer@example.com");
    }

    [Fact]
    [Trait("ScenarioId", "STEP15-INVARIANT-DB-FAILURE-162")]
    public async Task PostIntent_DbUpdateException_ReturnsRetryable503WithoutLeakageOrSuccess()
    {
        const string sensitiveFailure =
            "Server=private-db;******; raw-password; controlled save failure";
        var service = new ControlledPaymentService
        {
            Create = _ => throw new DbUpdateException(sensitiveFailure)
        };
        var logger = new FullExceptionCapturingLogger();
        var token = CatalogTestSupport.CreateToken(162);
        var device = CatalogTestSupport.CreateDevice(
            token,
            DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(service, [device], logger);
        using var client = factory.CreateApiClient();
        using var request = CreateValidRequest(token, "?language=he");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        await AssertPaymentProblemAsync(
            response,
            HttpStatusCode.ServiceUnavailable,
            "service_unavailable",
            "he");
        service.CreateCalls.Should().Be(1);
        service.GetCalls.Should().Be(0);
        body.Should().NotContainAny(
            sensitiveFailure,
            "private-db",
            "raw-password",
            "controlled save failure");
        logger.AllText.Should().NotContainAny(
            sensitiveFailure,
            "private-db",
            "raw-password",
            "controlled save failure");
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("he")]
    public async Task PostIntent_ModelBindingFieldErrorsAreNormalizedAndLocaleInvariant(string language)
    {
        var token = CatalogTestSupport.CreateToken(95);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = CreateFactory(new ControlledPaymentService(), [device]);
        using var client = factory.CreateApiClient();
        using var request = CreateValidRequest(token, $"?language={language}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));
        request.Content = new StringContent(
            """{"bookingId":"not-a-guid","method":"Card"}""",
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);
        var root = await AssertPaymentProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            CustomerPaymentErrorCodes.Invalid,
            language);

        root.GetProperty("fieldErrors").EnumerateObject().Should().ContainSingle();
        root.GetProperty("fieldErrors").GetProperty("bookingId")[0].GetString()
            .Should().Be(CustomerPaymentErrorCodes.Invalid);
    }

    private static async Task AssertAuthenticationProblemAsync(string? authorization)
    {
        var token = CatalogTestSupport.CreateToken(91);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        await using var factory = new CheckoutDraftApiFactory(devices: [device]);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/payments/intents?language=he");
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        request.Headers.Add("X-Device-Token", token);
        request.Headers.Add("Idempotency-Key", "valid-key");
        request.Content = new StringContent(
            """{"bookingId":"11111111-1111-1111-1111-111111111111","method":"Card"}""",
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        await AssertPaymentProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "customer_authentication_required",
            "he");
        response.Headers.WwwAuthenticate.Should().ContainSingle()
            .Which.Scheme.Should().Be("Bearer");
    }

    private static CheckoutDraftApiFactory CreateFactory(
        ControlledPaymentService service,
        IEnumerable<GhseeliApis.Models.CustomerDevice>? devices = null,
        IAppLogger? logger = null,
        string environmentName = "Development") =>
        new(
            devices: devices,
            environmentName: environmentName,
            configureTestServices: services =>
            {
                services.RemoveAll<ICustomerPaymentService>();
                services.AddSingleton<ICustomerPaymentService>(service);
                if (logger is not null)
                {
                    services.RemoveAll<IAppLogger>();
                    services.AddSingleton(logger);
                }
            });

    private static HttpRequestMessage CreateAuthenticatedPaymentRequest(
        HttpMethod method,
        string path,
        string deviceToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));
        request.Headers.Add("X-Device-Token", deviceToken);
        return request;
    }

    private static CustomerPaymentResponse CreatePaymentResponse(Guid paymentId) =>
        new()
        {
            Id = paymentId,
            BookingId = Guid.NewGuid(),
            Amount = 20m,
            Currency = "ILS",
            Method = "Card",
            Status = "Pending",
            Provider = PaymentProviders.Lahza,
            ProviderStatus = "pending",
            ProviderReference = "GHSEELI-GATE",
            CheckoutUrl = "https://checkout.lahza.test/pay/GHSEELI-GATE",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

    private static void AssertBearerSecurity(JsonElement operation)
    {
        var security = operation.GetProperty("security");
        security.GetArrayLength().Should().Be(1);
        security[0].TryGetProperty("CustomerBearer", out var scopes).Should().BeTrue();
        scopes.GetArrayLength().Should().Be(0);
        security[0].TryGetProperty("DeviceToken", out var deviceScopes).Should().BeTrue();
        deviceScopes.GetArrayLength().Should().Be(0);
    }

    private sealed class FullExceptionCapturingLogger : IAppLogger
    {
        private readonly List<string> _entries = [];

        public string AllText => string.Join(Environment.NewLine, _entries);

        public void LogInfo(string message) => _entries.Add(message);
        public void LogWarning(string message) => _entries.Add(message);
        public void LogError(string message) => _entries.Add(message);
        public void LogError(string message, Exception exception) =>
            _entries.Add($"{message}{Environment.NewLine}{exception}");
    }

    private static async Task<JsonElement> AssertPaymentProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        string language)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType
            .Should().Be("application/problem+json");
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var propertyNames = root.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        propertyNames.Should().Contain(
            ["type", "title", "status", "detail", "code", "language", "correlationId"]);
        propertyNames.Should().BeSubsetOf(
            ["type", "title", "status", "detail", "code", "language", "correlationId", "fieldErrors"]);
        root.GetProperty("type").GetString()
            .Should().Be($"https://api.ghseeli.example/errors/{code}");
        root.GetProperty("status").GetInt32().Should().Be((int)status);
        root.GetProperty("code").GetString().Should().Be(code);
        root.GetProperty("language").GetString().Should().Be(language);
        root.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
        var correlation = root.GetProperty("correlationId").GetString();
        correlation.Should().NotBeNullOrWhiteSpace();
        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle(correlation);
        response.Content.Headers.ContentLanguage.Should().ContainSingle(language);
        body.Should().NotContain("System.")
            .And.NotContain("Exception")
            .And.NotContain("sk_test_")
            .And.NotContain("lahza_secret_")
            .And.NotContain("SELECT ");
        return root.Clone();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        string body,
        string contentType,
        string? idempotencyKey)
    {
        var token = CatalogTestSupport.CreateToken(91);
        var device = CatalogTestSupport.CreateDevice(token, DateTimeOffset.UtcNow.AddDays(1));
        var factory = new CheckoutDraftApiFactory(devices: [device]);
        var client = factory.CreateApiClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/intents");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateJwt(Guid.NewGuid()));
        request.Headers.Add("X-Device-Token", token);
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }
        request.Content = new StringContent(body, Encoding.UTF8, contentType);
        var response = await client.SendAsync(request);
        request.Dispose();
        client.Dispose();
        await factory.DisposeAsync();
        return response;
    }

    private static HttpRequestMessage CreateValidRequest(
        string deviceToken,
        string query = "")
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/payments/intents{query}");
        request.Headers.Add("X-Device-Token", deviceToken);
        request.Headers.Add("Idempotency-Key", "valid-key");
        request.Content = new StringContent(
            """{"bookingId":"11111111-1111-1111-1111-111111111111","method":"Card"}""",
            Encoding.UTF8,
            "application/json");
        return request;
    }

    private static string CreateJwt(
        Guid userId,
        string role = "User",
        DateTime? expires = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "GhseeliApis.CheckoutDraftTests",
            audience: "GhseeliApis.CheckoutDraftClients",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
                new Claim(ClaimTypes.Role, role)
            ],
            expires: expires ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _content;

        public UnknownLengthContent(byte[] content)
        {
            _content = content;
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            stream.WriteAsync(_content).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ControlledPaymentService : ICustomerPaymentService
    {
        public int CreateCalls { get; private set; }
        public int GetCalls { get; private set; }
        public CreateCustomerPaymentIntentRequest? LastRequest { get; private set; }
        public Func<CreateCustomerPaymentIntentRequest, CustomerPaymentResponse>? Create { get; set; }
        public Func<Guid, CustomerPaymentResponse?>? Get { get; set; }

        public Task<CustomerPaymentResponse> CreateAsync(
            CreateCustomerPaymentIntentRequest request,
            string idempotencyKey,
            Guid userId,
            Guid deviceId,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            LastRequest = request;
            return Task.FromResult(Create?.Invoke(request)
                ?? throw new CustomerPaymentException(
                    404,
                    CustomerPaymentErrorCodes.BookingNotFound));
        }

        public Task<CustomerPaymentResponse?> GetAsync(
            Guid paymentId,
            Guid userId,
            Guid deviceId,
            CancellationToken cancellationToken)
        {
            GetCalls++;
            return Task.FromResult(Get?.Invoke(paymentId));
        }

        public Task<CustomerPaymentResponse?> VerifyAsync(
            Guid paymentId,
            Guid userId,
            Guid deviceId,
            CancellationToken cancellationToken) =>
            GetAsync(paymentId, userId, deviceId, cancellationToken);
    }
}
