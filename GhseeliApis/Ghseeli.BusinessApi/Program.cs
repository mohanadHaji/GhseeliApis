using Ghseeli.BusinessApi.Constants;
using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Ghseeli.BusinessApi.Repositories;
using Ghseeli.BusinessApi.Repositories.Interfaces;
using Ghseeli.BusinessApi.Services;
using Ghseeli.BusinessApi.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
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

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(BusinessPolicies.BusinessMember, policy =>
        policy.RequireRole(BusinessRoles.Owner, BusinessRoles.Employee, BusinessRoles.Admin));
    options.AddPolicy(BusinessPolicies.OwnerOrAdmin, policy =>
        policy.RequireRole(BusinessRoles.Owner, BusinessRoles.Admin));
});

builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<IBusinessAuthService, BusinessAuthService>();
builder.Services.AddScoped<ICompanyProfileService, CompanyProfileService>();

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

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/api/health", () => Results.Text("Healthy - Ghseeli Business API"));

if (swaggerEnabled)
{
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.Run();

public partial class Program;
