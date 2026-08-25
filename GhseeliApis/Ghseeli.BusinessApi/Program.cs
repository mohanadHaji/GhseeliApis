using FluentValidation;
using FluentValidation.AspNetCore;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.InternalServices;
using Ghseeli.BusinessApi.Infrastructure;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Repositories;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services.Availability;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Catalog;
using Ghseeli.BusinessApi.Services.Interfaces;
using Ghseeli.BusinessApi.Services.Validation.Availability;
using Ghseeli.BusinessApi.Services.Validation.Auth;
using Ghseeli.BusinessApi.Services.Validation.Catalog;
using Ghseeli.BusinessApi.Services.Validation.Companies;
using Ghseeli.BusinessApi.Swagger;
using Ghseeli.Common.Logging;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var validationJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
validationJsonOptions.Converters.Add(new JsonStringEnumConverter(
    namingPolicy: null,
    allowIntegerValues: false));

builder.Services.AddControllers(options =>
    {
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(
            namingPolicy: null,
            allowIntegerValues: false));
    });
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors
                    .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                        ? "The input was not valid."
                        : error.ErrorMessage)
                    .ToArray(),
                StringComparer.Ordinal);

        return new ContentResult
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentType = "application/json",
            Content = JsonSerializer.Serialize(new
            {
                title = "One or more validation errors occurred.",
                status = StatusCodes.Status400BadRequest,
                errors
            }, validationJsonOptions)
        };
    };
});
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.ExcludedHosts.Clear();
});
builder.Services.AddSwaggerGen(options =>
{
    options.SupportNonNullableReferenceTypes();
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Ghseeli Business API",
        Version = "v1",
        Description = "Independent API for Ghseeli business owners and staff."
    });
    options.AddSecurityDefinition("BusinessBearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter the Business API JWT."
    });
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Legacy display alias for the BusinessBearer scheme."
    });
    foreach (var definition in new[]
    {
        ("HmacServiceId", InternalServiceWireConstants.ServiceIdHeaderName, "<service-id>"),
        ("HmacTimestamp", InternalServiceWireConstants.TimestampHeaderName, "<utc-iso-timestamp>"),
        ("HmacNonce", InternalServiceWireConstants.NonceHeaderName, "<unique-nonce>"),
        ("HmacSignature", InternalServiceWireConstants.SignatureHeaderName, "<hex-hmac-signature>")
    })
    {
        options.AddSecurityDefinition(definition.Item1,
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Name = definition.Item2,
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Description = $"{definition.Item3}; part of the canonical HMAC request and replay protection contract."
            });
    }
    options.SchemaFilter<StringEnumSchemaFilter>();
    options.SchemaFilter<BusinessRequestSchemaFilter>();
    options.OperationFilter<BusinessOperationFilter>();
    options.DocumentFilter<BusinessDocumentFilter>();
});
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<InternalServiceAuthenticationOptions>,
    InternalServiceAuthenticationOptionsValidator>();
var internalAuthenticationOptions = builder.Services
    .AddOptions<InternalServiceAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(InternalServiceAuthenticationOptions.SectionName));
if (builder.Configuration
    .GetSection(InternalServiceAuthenticationOptions.SectionName)
    .GetSection("Services")
    .GetChildren()
    .Any())
{
    internalAuthenticationOptions.ValidateOnStart();
}
var useForwardedHeaders =
    builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").GetChildren().Any() ||
    builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").GetChildren().Any();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownProxies.Clear();
    options.KnownNetworks.Clear();

    foreach (var knownProxy in builder.Configuration
                 .GetSection("ForwardedHeaders:KnownProxies")
                 .Get<string[]>() ?? Array.Empty<string>())
    {
        if (IPAddress.TryParse(knownProxy, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }

    foreach (var knownNetwork in builder.Configuration
                 .GetSection("ForwardedHeaders:KnownNetworks")
                 .Get<string[]>() ?? Array.Empty<string>())
    {
        var parts = knownNetwork.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            IPAddress.TryParse(parts[0], out var prefix) &&
            int.TryParse(parts[1], out var prefixLength))
        {
            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength));
        }
    }
});
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<BusinessRateLimitingOptions>,
    BusinessRateLimitingOptionsValidator>();
builder.Services
    .AddOptions<BusinessRateLimitingOptions>()
    .Bind(builder.Configuration.GetSection(BusinessRateLimitingOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(
            BusinessRateLimitPartitioner.GetPrimaryPartition),
        PartitionedRateLimiter.Create<HttpContext, string>(
            BusinessRateLimitPartitioner.GetAuthenticationAggregatePartition));
    options.OnRejected = BusinessRateLimitingResponse.WriteAsync;
});

var businessConnection = builder.Configuration.GetConnectionString("BusinessConnection");
if (string.IsNullOrWhiteSpace(businessConnection))
{
    throw new InvalidOperationException(
        "Business database connection is not configured. Set ConnectionStrings__BusinessConnection.");
}

builder.Services.AddOptions<BusinessSchemaOptions>()
    .Bind(builder.Configuration.GetSection(BusinessSchemaOptions.SectionName))
    .Validate(
        options => string.Equals(
            options.DefaultSchema,
            BusinessSchemaOptions.OwnedDefaultSchema,
            StringComparison.Ordinal),
        "BusinessSchema:DefaultSchema must identify the owned dbo schema.")
    .ValidateOnStart();

builder.Services.AddDbContext<BusinessDbContext>(options =>
    options.UseSqlServer(
        businessConnection,
        sqlServer => sqlServer.MigrationsHistoryTable(
            BusinessSchemaOptions.MigrationsHistoryTable,
            BusinessSchemaOptions.OwnedDefaultSchema)));

builder.Services
    .AddIdentityCore<BusinessUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddSignInManager()
    .AddEntityFrameworkStores<BusinessDbContext>()
    .AddDefaultTokenProviders();

var businessJwtSettings = builder.Configuration.GetSection("BusinessJwtSettings");
var businessJwtSecret = businessJwtSettings["SecretKey"];
if (string.IsNullOrWhiteSpace(businessJwtSecret))
{
    throw new InvalidOperationException(
        "Business JWT secret is not configured. Set BusinessJwtSettings__SecretKey.");
}

var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = BusinessAuthenticationSchemes.Combined;
    options.DefaultAuthenticateScheme = BusinessAuthenticationSchemes.Combined;
    options.DefaultChallengeScheme = BusinessAuthenticationSchemes.Combined;
    options.DefaultForbidScheme = BusinessAuthenticationSchemes.Combined;
});

authenticationBuilder.AddPolicyScheme(
    BusinessAuthenticationSchemes.Combined,
    "Combined Business authentication",
    options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Path.StartsWithSegments("/api/v1/internal", StringComparison.OrdinalIgnoreCase)
                ? BusinessAuthenticationSchemes.InternalService
                : JwtBearerDefaults.AuthenticationScheme;
    });

authenticationBuilder.AddJwtBearer(
    JwtBearerDefaults.AuthenticationScheme,
    options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = businessJwtSettings["Issuer"],
            ValidAudience = businessJwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(businessJwtSecret)),
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.HandleResponse();
                return BusinessAuthenticationProblemResponseFactory.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    "Authentication is required.",
                    "A valid Business API access token is required.",
                    BusinessAuthenticationProblemCodes.AuthenticationRequired);
            },
            OnForbidden = context =>
                BusinessAuthenticationProblemResponseFactory.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    "Access is forbidden.",
                    "The authenticated principal is not authorized for this operation.",
                    BusinessAuthenticationProblemCodes.AuthorizationForbidden)
        };
    });

authenticationBuilder.AddScheme<AuthenticationSchemeOptions, InternalServiceAuthenticationHandler>(
    BusinessAuthenticationSchemes.InternalService,
    _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(BusinessPolicies.BusinessMember, policy =>
        policy.RequireRole(BusinessRoles.Owner, BusinessRoles.Employee, BusinessRoles.Admin));
    options.AddPolicy(BusinessPolicies.OwnerOrAdmin, policy =>
        policy.RequireRole(BusinessRoles.Owner, BusinessRoles.Admin));
    options.AddPolicy(BusinessPolicies.Admin, policy =>
        policy.RequireRole(BusinessRoles.Admin));
    options.AddPolicy(BusinessPolicies.InternalCatalogRead, policy =>
    {
        policy.AddAuthenticationSchemes(BusinessAuthenticationSchemes.InternalService);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(
            BusinessClaimTypes.InternalAllowedOperation,
            InternalServiceOperationNames.CatalogSnapshot);
    });
    options.AddPolicy(BusinessPolicies.InternalAppointmentValidate, policy =>
    {
        policy.AddAuthenticationSchemes(BusinessAuthenticationSchemes.InternalService);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(
            BusinessClaimTypes.InternalAllowedOperation,
            InternalServiceOperationNames.AppointmentValidate);
    });
    options.AddPolicy(BusinessPolicies.InternalReservationCreate, policy =>
    {
        policy.AddAuthenticationSchemes(BusinessAuthenticationSchemes.InternalService);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(
            BusinessClaimTypes.InternalAllowedOperation,
            InternalServiceOperationNames.ReservationCreate);
    });
    options.AddPolicy(BusinessPolicies.InternalReservationStatusRead, policy =>
    {
        policy.AddAuthenticationSchemes(BusinessAuthenticationSchemes.InternalService);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim(
            BusinessClaimTypes.InternalAllowedOperation,
            InternalServiceOperationNames.ReservationStatusRead);
    });
});

builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<ICatalogRepository, CatalogRepository>();
builder.Services.AddScoped<IAvailabilityRepository, AvailabilityRepository>();
builder.Services.AddScoped<IInternalServiceNonceStore, InternalServiceNonceStore>();
builder.Services.AddScoped<IInternalIdempotencyStore, InternalIdempotencyStore>();
builder.Services.AddScoped<InternalServiceRequestValidator>();
builder.Services.AddScoped<IBusinessAuthRequestValidator, BusinessAuthRequestValidator>();
builder.Services.AddScoped<ICompanyRequestValidator, CompanyRequestValidator>();
builder.Services.AddScoped<ICatalogRequestValidator, CatalogRequestValidator>();
builder.Services.AddScoped<IAvailabilityRequestValidator, AvailabilityRequestValidator>();
builder.Services.AddScoped<IInternalAppointmentRequestValidator, InternalAppointmentRequestValidator>();
builder.Services.AddScoped<ICatalogRuleValidator, CatalogRuleValidator>();
builder.Services.AddScoped<IAvailabilityRuleValidator, AvailabilityRuleValidator>();
builder.Services.AddScoped<ICatalogSelectionValidator, CatalogSelectionValidator>();
builder.Services.AddScoped<ITimeZoneAvailabilityResolver, TimeZoneAvailabilityResolver>();
builder.Services.AddScoped<IServiceAreaCalculator, ServiceAreaCalculator>();
builder.Services.AddScoped<IBusinessAuthService, BusinessAuthService>();
builder.Services.AddScoped<ICompanyProfileService, CompanyProfileService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IAvailabilityManagementService, AvailabilityManagementService>();
builder.Services.AddScoped<ICatalogPublicationService, CatalogPublicationService>();
builder.Services.AddScoped<IAppointmentValidationService, AppointmentValidationService>();
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<IBookingStatusService, BookingStatusService>();
builder.Services.AddScoped<IBookingStatusOutboxDispatcher, BookingStatusOutboxDispatcher>();
builder.Services.AddScoped<IBookingStatusDeadLetterService, BookingStatusDeadLetterService>();
builder.Services.Configure<BookingStatusOutboxOptions>(
    builder.Configuration.GetSection(BookingStatusOutboxOptions.SectionName));
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<CustomerBookingStatusClientOptions>,
    CustomerBookingStatusClientOptionsValidator>();
builder.Services
    .AddOptions<CustomerBookingStatusClientOptions>()
    .Bind(builder.Configuration.GetSection(CustomerBookingStatusClientOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddHttpClient<ICustomerBookingStatusClient, CustomerBookingStatusClient>((provider, client) =>
{
    var options = provider.GetRequiredService<
        Microsoft.Extensions.Options.IOptions<CustomerBookingStatusClientOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
});
builder.Services.AddHostedService<BookingStatusOutboxWorker>();
builder.Services.AddSingleton<Ghseeli.BusinessApi.Services.Availability.ISystemClock, Ghseeli.BusinessApi.Services.Availability.SystemClock>();
builder.Services.AddSingleton<IAppLogger, ConsoleLogger>();

var app = builder.Build();

var swaggerEnabled = app.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("Swagger:Enabled");

if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<BusinessHttpContractMiddleware>();

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Ghseeli Business API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseRouting();
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api/v1/internal", StringComparison.OrdinalIgnoreCase),
    branch => branch.UseHttpsRedirection());
app.UseAuthentication();
app.UseMiddleware<BusinessRateLimitPartitionMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<InternalRequestIdempotencyMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/api/health", async ([FromServices] BusinessDbContext context) =>
{
    var healthy = false;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    try
    {
        if (!context.Database.IsRelational())
        {
            _ = await context.Companies.AsNoTracking().AnyAsync(timeout.Token);
            healthy = true;
        }
        else if (await context.Database.CanConnectAsync(timeout.Token))
        {
            _ = await context.Companies.AsNoTracking().AnyAsync(timeout.Token);
            var expected = context.Database.GetMigrations().ToArray();
            var applied = (await context.Database.GetAppliedMigrationsAsync(timeout.Token)).ToArray();
            var pending = (await context.Database.GetPendingMigrationsAsync(timeout.Token)).ToArray();
            healthy = expected.SequenceEqual(applied, StringComparer.Ordinal) &&
                      pending.Length == 0;
        }
    }
    catch
    {
        healthy = false;
    }

    var payload = System.Text.Json.JsonSerializer.Serialize(new
    {
        status = healthy ? "Healthy" : "Unhealthy",
        service = "Ghseeli Business API",
        timestamp = DateTime.UtcNow.ToString(
            "O",
            System.Globalization.CultureInfo.InvariantCulture),
        version = "v1"
    });
    return Results.Text(
        payload,
        "application/json",
        statusCode: healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable);
});
app.MapMethods("/api/health", [HttpMethods.Head], () => Results.Ok())
    .ExcludeFromDescription();

if (swaggerEnabled)
{
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.Run();

public partial class Program;
