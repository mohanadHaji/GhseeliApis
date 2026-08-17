using GhseeliApis.Extensions;
using GhseeliApis.Persistence;
using GhseeliApis.Handlers;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger;
using GhseeliApis.Logger.Interfaces;
using GhseeliApis.Models;
using GhseeliApis.Repositories;
using GhseeliApis.Repositories.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
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