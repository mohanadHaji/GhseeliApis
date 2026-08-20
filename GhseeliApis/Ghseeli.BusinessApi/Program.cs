using FluentValidation;
using FluentValidation.AspNetCore;
using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.InternalServices;
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
using Ghseeli.IntegrationContracts.InternalHttp;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using System.Net;

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
builder.Services.AddSwaggerGen(options =>
{
    options.SupportNonNullableReferenceTypes();
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Ghseeli Business API",
        Version = "v1",
        Description = "Independent API for Ghseeli business owners and staff."
    });
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter the Business API JWT."
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
    options.SchemaFilter<StringEnumSchemaFilter>();
    options.SchemaFilter<BusinessRequestSchemaFilter>();
});
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<InternalServiceAuthenticationOptions>,
    InternalServiceAuthenticationOptionsValidator>();
builder.Services
    .AddOptions<InternalServiceAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(InternalServiceAuthenticationOptions.SectionName))
    .ValidateOnStart();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;

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

var businessConnection = builder.Configuration.GetConnectionString("BusinessConnection");
if (string.IsNullOrWhiteSpace(businessConnection))
{
    throw new InvalidOperationException(
        "Business database connection is not configured. Set ConnectionStrings__BusinessConnection.");
}

builder.Services.AddDbContext<BusinessDbContext>(options =>
    options.UseSqlServer(businessConnection));

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
builder.Services.AddSingleton<Ghseeli.BusinessApi.Services.Availability.ISystemClock, Ghseeli.BusinessApi.Services.Availability.SystemClock>();
builder.Services.AddSingleton<IAppLogger, ConsoleLogger>();

var app = builder.Build();

var swaggerEnabled = app.Environment.IsDevelopment()
    || builder.Configuration.GetValue<bool>("Swagger:Enabled");

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Ghseeli Business API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseForwardedHeaders();
app.UseRouting();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api/v1/internal", StringComparison.OrdinalIgnoreCase),
    branch => branch.UseHttpsRedirection());
app.UseAuthentication();
app.UseMiddleware<InternalRequestIdempotencyMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/api/health", () => Results.Text("Healthy - Ghseeli Business API"));

if (swaggerEnabled)
{
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.Run();

public partial class Program;
