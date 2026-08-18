using Ghseeli.BusinessApi.Models;
using Ghseeli.BusinessApi.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
    .AddIdentity<BusinessUser, IdentityRole<Guid>>()
    .AddEntityFrameworkStores<BusinessDbContext>()
    .AddDefaultTokenProviders();

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
