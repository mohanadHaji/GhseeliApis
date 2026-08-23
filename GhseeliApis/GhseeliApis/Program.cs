using FluentValidation;
using FluentValidation.AspNetCore;
using GhseeliApis.Extensions;
using GhseeliApis.Middleware;
using GhseeliApis.Persistence;
using GhseeliApis.Handlers;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Services.Business;
using GhseeliApis.Services.Catalog;
using GhseeliApis.Services.Checkout;
using GhseeliApis.Services.Configuration;
using GhseeliApis.Services.Devices;
using GhseeliApis.Services.Bookings;
using GhseeliApis.Services.Internal;
using Ghseeli.Common.Logging;
using GhseeliApis.Models;
using Ghseeli.IntegrationContracts.Bookings;
using Ghseeli.IntegrationContracts.InternalHttp;
using GhseeliApis.Repositories;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers(options =>
{
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    var defaultFactory = options.InvalidModelStateResponseFactory;
    options.InvalidModelStateResponseFactory = context =>
    {
        if (!IsPricingRepricePath(context.HttpContext.Request.Path) &&
            !IsBookingConfirmationPath(context.HttpContext.Request.Path) &&
            !IsBookingStatusCallbackPath(context.HttpContext.Request.Path))
        {
            return defaultFactory(context);
        }

        var unsupportedMediaType = context.ModelState.Values
            .SelectMany(entry => entry.Errors)
            .Any(error => error.Exception is UnsupportedContentTypeException);
        var statusCode = unsupportedMediaType
            ? StatusCodes.Status415UnsupportedMediaType
            : StatusCodes.Status400BadRequest;
        var request = context.HttpContext.Request;
        var language = ConfigurationLanguageResolver.Resolve(
            request.Query["language"].ToString(),
            request.Headers.AcceptLanguage.ToString());
        var bookingStatusRoute = IsBookingStatusCallbackPath(request.Path);
        var bookingRoute = IsBookingConfirmationPath(request.Path);
        var problem = bookingStatusRoute
            ? BookingStatusProblemDetailsFactory.Create(
                statusCode,
                unsupportedMediaType
                    ? BookingStatusErrorCodes.UnsupportedMediaType
                    : BookingStatusErrorCodes.Invalid,
                language,
                context.HttpContext.TraceIdentifier)
            : bookingRoute
            ? BookingConfirmationProblemDetailsFactory.Create(
                statusCode,
                unsupportedMediaType
                    ? BookingConfirmationProblemCodes.UnsupportedMediaType
                    : BookingConfirmationProblemCodes.Invalid,
                language,
                context.HttpContext.TraceIdentifier)
            : CheckoutPricingProblemDetailsFactory.Create(
                statusCode,
                unsupportedMediaType
                    ? CheckoutPricingProblemCodes.UnsupportedMediaType
                    : CheckoutPricingProblemCodes.Invalid,
                language,
                context.HttpContext.TraceIdentifier);
        context.HttpContext.Response.Headers.CacheControl = "no-store";

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    };
});
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Ghseeli APIs",
        Version = "v1",
        Description = "A simple ASP.NET Core Web API with SQL Server and ASP.NET Core Identity"
    });
});

// Add SQL Server
builder.Services.AddSqlServer(builder.Configuration);

// Configure ASP.NET Core Identity
builder.Services.AddIdentity<User, IdentityRole<Guid>>(options =>
{
    // Password settings
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;
    options.Password.RequiredUniqueChars = 1;

    // Lockout settings
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User settings
    options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
    options.User.RequireUniqueEmail = true;

    // Sign-in settings
    options.SignIn.RequireConfirmedEmail = false;
    options.SignIn.RequireConfirmedPhoneNumber = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

// Configure JWT Authentication (REQUIRED)
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"];

if (string.IsNullOrEmpty(secretKey))
{
    throw new InvalidOperationException("JWT SecretKey is not configured. Set JwtSettings__SecretKey environment variable.");
}

var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
});

authenticationBuilder.AddJwtBearer(options =>
{
    options.SaveToken = true;
    options.RequireHttpsMetadata = builder.Environment.IsProduction(); // True in production, false in development
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero // Remove default 5 minute clock skew
    };
});

// Configure Google OAuth (OPTIONAL - only if credentials are provided)
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    authenticationBuilder.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.SaveTokens = true;
        options.CallbackPath = "/api/auth/google-callback";
    });
    Console.WriteLine("✅ Google OAuth configured successfully");
}
else
{
    Console.WriteLine("⚠️  Google OAuth not configured - Google login will not be available");
}

// Configure Facebook OAuth (OPTIONAL - only if credentials are provided)
var facebookAppId = builder.Configuration["Authentication:Facebook:AppId"];
var facebookAppSecret = builder.Configuration["Authentication:Facebook:AppSecret"];

if (!string.IsNullOrEmpty(facebookAppId) && !string.IsNullOrEmpty(facebookAppSecret))
{
    authenticationBuilder.AddFacebook(options =>
    {
        options.AppId = facebookAppId;
        options.AppSecret = facebookAppSecret;
        options.SaveTokens = true;
        options.CallbackPath = "/api/auth/facebook-callback";
        options.Fields.Add("name");
        options.Fields.Add("email");
        options.Fields.Add("picture");
    });
    Console.WriteLine("✅ Facebook OAuth configured successfully");
}
else
{
    Console.WriteLine("⚠️  Facebook OAuth not configured - Facebook login will not be available");
}

// Configure Authorization Policies
builder.Services.AddAuthorization(options =>
{
    // User policy - requires User role
    options.AddPolicy("UserPolicy", policy => policy.RequireRole("User"));
    
    // Company policy - requires Company role
    options.AddPolicy("CompanyPolicy", policy => policy.RequireRole("Company"));
    
    // Admin policy - requires Admin role
    options.AddPolicy("AdminPolicy", policy => policy.RequireRole("Admin"));
    
    // UserOrCompany policy - requires either User or Company role
    options.AddPolicy("UserOrCompanyPolicy", policy => policy.RequireRole("User", "Company"));
    
    // CompanyOrAdmin policy - requires either Company or Admin role
    options.AddPolicy("CompanyOrAdminPolicy", policy => policy.RequireRole("Company", "Admin"));
});

// Register Repositories
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IHealthRepository, HealthRepository>();
builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<IVehicleRepository, VehicleRepository>();
builder.Services.AddScoped<IUserAddressRepository, UserAddressRepository>();
builder.Services.AddScoped<IServiceRepository, ServiceRepository>();
builder.Services.AddScoped<IServiceOptionRepository, ServiceOptionRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IWalletRepository, WalletRepository>();
builder.Services.AddScoped<IWalletTransactionRepository, WalletTransactionRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<ICompanyAvailabilityRepository, CompanyAvailabilityRepository>();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<ICustomerConfigurationRepository, CustomerConfigurationRepository>();
builder.Services.AddScoped<ICatalogReadModelRepository, CatalogReadModelRepository>();
builder.Services.AddScoped<ICheckoutDraftRepository, CheckoutDraftRepository>();

// Register Handlers
builder.Services.AddScoped<IUserHandler, UserHandler>();
builder.Services.AddScoped<IHealthHandler, HealthHandler>();
builder.Services.AddScoped<IVehicleHandler, VehicleHandler>();
builder.Services.AddScoped<IUserAddressHandler, UserAddressHandler>();
builder.Services.AddScoped<IBookingHandler, BookingHandler>();
builder.Services.AddScoped<ICompanyHandler, CompanyHandler>();
builder.Services.AddScoped<IServiceHandler, ServiceHandler>();
builder.Services.AddScoped<IServiceOptionHandler, ServiceOptionHandler>();
builder.Services.AddScoped<IPaymentHandler, PaymentHandler>();

// Register Services
builder.Services.AddScoped<GhseeliApis.Services.Interfaces.IAuthService, GhseeliApis.Services.AuthService>();
builder.Services.AddScoped<GhseeliApis.Services.Interfaces.IPaymentGatewayService, GhseeliApis.Services.StripePaymentService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IDeviceTokenGenerator, DeviceTokenGenerator>();
builder.Services.AddScoped<IDeviceRegistrationService, DeviceRegistrationService>();
builder.Services.AddScoped<ICustomerConfigurationService, CustomerConfigurationService>();
builder.Services.AddScoped<ICatalogProviderRefreshCoordinator, CatalogProviderRefreshCoordinator>();
builder.Services.AddScoped<ICatalogReadModelService, CatalogReadModelService>();
builder.Services.AddScoped<ICheckoutDraftService, CheckoutDraftService>();
builder.Services.AddScoped<ICheckoutPricingService, CheckoutPricingService>();
builder.Services.AddScoped<IBookingConfirmationService, BookingConfirmationService>();
builder.Services.AddScoped<IBookingStatusInboxService, BookingStatusInboxService>();
builder.Services.AddScoped<
    ICustomerInternalIdempotencyCleanupService,
    CustomerInternalIdempotencyCleanupService>();
builder.Services.AddScoped<
    ICustomerInternalIdempotencyLeaseService,
    CustomerInternalIdempotencyLeaseService>();
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<CustomerInternalServiceOptions>,
    CustomerInternalServiceOptionsValidator>();
builder.Services.AddOptions<CustomerInternalServiceOptions>()
    .Bind(builder.Configuration.GetSection(CustomerInternalServiceOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<ICheckoutPaymentCapabilitiesService, CheckoutPaymentCapabilitiesService>();
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<DeviceTokenOptions>,
    DeviceTokenOptionsValidator>();
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<CatalogReadModelOptions>,
    CatalogReadModelOptionsValidator>();
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<CheckoutDraftOptions>,
    CheckoutDraftOptionsValidator>();
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<CheckoutPricingOptions>,
    CheckoutPricingOptionsValidator>();
builder.Services.AddOptions<DeviceTokenOptions>()
    .Bind(builder.Configuration.GetSection(DeviceTokenOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<CatalogReadModelOptions>()
    .Bind(builder.Configuration.GetSection(CatalogReadModelOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<CheckoutDraftOptions>()
    .Bind(builder.Configuration.GetSection(CheckoutDraftOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<CheckoutPricingOptions>()
    .Bind(builder.Configuration.GetSection(CheckoutPricingOptions.SectionName))
    .ValidateOnStart();
builder.Services.Configure<StripeConfigurationOptions>(
    builder.Configuration.GetSection(StripeConfigurationOptions.SectionName));
builder.Services.Configure<BusinessApiClientOptions>(
    builder.Configuration.GetSection(BusinessApiClientOptions.SectionName));
builder.Services.AddTransient<BusinessApiResilienceDelegatingHandler>();
builder.Services.AddTransient<CorrelationIdPropagationHandler>();
builder.Services.AddTransient<HmacSigningDelegatingHandler>();
builder.Services.AddHttpClient<IBusinessApiClient, BusinessApiClient>((serviceProvider, client) =>
    {
        var options = serviceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<BusinessApiClientOptions>>()
            .Value;

        if (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            client.BaseAddress = baseUri;
        }

        client.Timeout = Timeout.InfiniteTimeSpan;
    })
    .AddHttpMessageHandler<BusinessApiResilienceDelegatingHandler>()
    .AddHttpMessageHandler<CorrelationIdPropagationHandler>()
    .AddHttpMessageHandler<HmacSigningDelegatingHandler>();

// Register Logger
builder.Services.AddSingleton<IAppLogger, ConsoleLogger>();

var app = builder.Build();

// Configure the HTTP request pipeline
// Enable Swagger in Development or when explicitly enabled for a deployed environment.
var swaggerEnabled = app.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("Swagger:Enabled");

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Ghseeli APIs v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseMiddleware<CorrelationIdMiddleware>();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments(
            "/api/v1/internal/bookings",
            StringComparison.OrdinalIgnoreCase,
            out var remaining) &&
        !IsKnownInternalBookingRouteShape(remaining))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});
app.UseMiddleware<CustomerInternalServiceMiddleware>();
app.Use(async (context, next) =>
{
    var bookingStatusRoute = string.Equals(
        context.GetEndpoint()?
            .Metadata.GetMetadata<CustomerInternalOperationAttribute>()?.Operation,
        InternalServiceOperationNames.BookingStatusCallback,
        StringComparison.Ordinal);
    if (!IsBookingConfirmationPath(context.Request.Path) && !bookingStatusRoute)
    {
        await next();
        return;
    }

    context.Response.OnStarting(() =>
    {
        context.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    });

    if (bookingStatusRoute && !IsJsonContentType(context.Request.ContentType))
    {
        await WriteBookingStatusProblemAsync(
            context,
            StatusCodes.Status415UnsupportedMediaType,
            BookingStatusErrorCodes.UnsupportedMediaType);
        return;
    }

    try
    {
        await next();
    }
    catch (BadHttpRequestException exception) when (!context.Response.HasStarted)
    {
        context.Response.Clear();
        var statusCode = exception.StatusCode;
        if (bookingStatusRoute)
        {
            await WriteBookingStatusProblemAsync(
                context,
                statusCode,
                statusCode == StatusCodes.Status413PayloadTooLarge
                    ? BookingStatusErrorCodes.RequestBodyTooLarge
                    : BookingStatusErrorCodes.Invalid);
        }
        else
        {
            var code = statusCode == StatusCodes.Status413PayloadTooLarge
                ? BookingConfirmationProblemCodes.RequestBodyTooLarge
                : BookingConfirmationProblemCodes.Invalid;
            await WriteBookingProblemAsync(context, statusCode, code);
        }
        return;
    }

    if (bookingStatusRoute)
    {
        return;
    }

    if (!context.Response.HasStarted &&
        context.Response.StatusCode >= StatusCodes.Status400BadRequest)
    {
        var code = context.Response.StatusCode switch
        {
            StatusCodes.Status413PayloadTooLarge =>
                BookingConfirmationProblemCodes.RequestBodyTooLarge,
            StatusCodes.Status415UnsupportedMediaType =>
                BookingConfirmationProblemCodes.UnsupportedMediaType,
            StatusCodes.Status503ServiceUnavailable =>
                BookingConfirmationProblemCodes.Unavailable,
            _ => BookingConfirmationProblemCodes.Invalid
        };
        await WriteBookingProblemAsync(context, context.Response.StatusCode, code);
    }
});
app.UseMiddleware<DeviceTokenMiddleware>();

// Add Authentication & Authorization middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

if (swaggerEnabled)
{
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

// Seed roles without preventing the API from starting when the database is temporarily unavailable.
try
{
    using var scope = app.Services.CreateScope();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    var logger = scope.ServiceProvider.GetRequiredService<IAppLogger>();

    string[] roles = ["User", "Company", "Admin"];
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            if (result.Succeeded)
            {
                logger.LogInfo($"Role '{role}' created successfully");
            }
            else
            {
                logger.LogError($"Failed to create role '{role}': {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }
    }
}
catch (Exception ex)
{
    app.Services.GetRequiredService<IAppLogger>()
        .LogError("Role seeding failed during startup. The API will continue running.", ex);
}

app.Run();

static bool IsPricingRepricePath(PathString path) =>
    path.Equals("/api/v1/pricing/reprice", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/api/v1/checkout/reprice", StringComparison.OrdinalIgnoreCase);

static bool IsBookingConfirmationPath(PathString path) =>
    path.StartsWithSegments("/api/v1/bookings", StringComparison.OrdinalIgnoreCase);

static bool IsBookingStatusCallbackPath(PathString path) =>
    path.Equals(
        "/api/v1/internal/bookings/status",
        StringComparison.OrdinalIgnoreCase);

static bool IsKnownInternalBookingRouteShape(PathString remaining)
{
    var segments = remaining.Value?
        .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? [];
    if (segments.Length == 1 &&
        string.Equals(segments[0], "status", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return segments.Length is 1 or 2 &&
        Guid.TryParse(segments[0], out _) &&
        (segments.Length == 1 ||
         string.Equals(segments[1], "reconcile", StringComparison.OrdinalIgnoreCase));
}

static bool IsJsonContentType(string? contentType)
{
    var mediaType = contentType?.Split(';', 2)[0].Trim();
    return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
        mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true;
}

static async Task WriteBookingProblemAsync(HttpContext context, int statusCode, string code)
{
    var language = ConfigurationLanguageResolver.Resolve(
        context.Request.Query["language"].ToString(),
        context.Request.Headers.AcceptLanguage.ToString());
    var problem = BookingConfirmationProblemDetailsFactory.Create(
        statusCode,
        code,
        language,
        context.TraceIdentifier);
    context.Response.StatusCode = statusCode;
    context.Response.ContentType = "application/problem+json";
    context.Response.Headers.CacheControl = "no-store";
    await context.Response.WriteAsJsonAsync(
        problem,
        options: null,
        contentType: "application/problem+json",
        cancellationToken: context.RequestAborted);
}

static async Task WriteBookingStatusProblemAsync(
    HttpContext context,
    int statusCode,
    string code)
{
    var language = ConfigurationLanguageResolver.Resolve(
        context.Request.Query["language"].ToString(),
        context.Request.Headers.AcceptLanguage.ToString());
    var problem = BookingStatusProblemDetailsFactory.Create(
        statusCode,
        code,
        language,
        context.TraceIdentifier);
    context.Response.StatusCode = statusCode;
    context.Response.ContentType = "application/problem+json";
    context.Response.Headers.CacheControl = "no-store";
    await context.Response.WriteAsJsonAsync(
        problem,
        options: null,
        contentType: "application/problem+json",
        cancellationToken: context.RequestAborted);
}

public partial class Program;
