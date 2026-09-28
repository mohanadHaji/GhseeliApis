using System.Security.Claims;
using FluentAssertions;
using Ghseeli.IntegrationContracts.DataPartitioning;
using GhseeliApis.DataPartitioning;
using GhseeliApis.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace GhseeliApis.Tests.DataPartitioning;

/// <summary>
/// Verifies trusted request partition selection for public Demo-only APIs.
/// </summary>
public sealed class CustomerDataPartitionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_DemoSeedOnlyEndpoint_WhenEnabled_IgnoresJwtAndUsesDemo()
    {
        var called = false;
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(DataPartitionNames.ClaimType, DataPartitionNames.Production),
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
            ], "TestAuth"))
        };
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new DemoSeedDataOnlyAttribute()),
            "demo-only"));
        var partition = new CustomerDataPartitionContext();
        var middleware = new CustomerDataPartitionMiddleware(
            _ => { called = true; return Task.CompletedTask; });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DemoData:PublicApisOnly"] = bool.TrueString
            })
            .Build();

        await middleware.InvokeAsync(context, partition, configuration);

        called.Should().BeTrue();
        partition.Partition.Should().Be(DataPartitionNames.Demo);
        context.User.Identity?.IsAuthenticated.Should().BeFalse();
    }
}
