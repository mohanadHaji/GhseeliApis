using GhseeliApis.Extensions;
using GhseeliApis.Data;
using GhseeliApis.Handlers;
using GhseeliApis.Handlers.Interfaces;
using GhseeliApis.Logger;
using GhseeliApis.Logger.Interfaces;

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
        Description = "A simple ASP.NET Core Web API with Google Cloud SQL"
    });
});

// Add Google Cloud SQL
builder.Services.AddGoogleCloudSql(builder.Configuration);

// Register Handlers
builder.Services.AddScoped<IUserHandler, UserHandler>();
builder.Services.AddScoped<IHealthHandler, HealthHandler>();

// Register Logger
builder.Services.AddSingleton<IAppLogger, ConsoleLogger>();

var app = builder.Build();

// Configure the HTTP request pipeline
// Enable Swagger only in Development mode
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Ghseeli APIs v1");
        options.RoutePrefix = string.Empty; // Set Swagger UI at the app's root
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();