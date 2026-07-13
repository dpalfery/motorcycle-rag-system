using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.API.Middleware;

namespace MotorcycleRAG.UnitTests.Presentation.API.Middleware;

public sealed class HostHeaderValidationMiddlewareTests
{
    [Fact]
    public void Constructor_WithNullDependenciesOrEmptyAllowlist_Throws()
    {
        RequestDelegate next = _ => Task.CompletedTask;
        var configuration = CreateConfiguration("api.example.com");

        ((Action)(() => new HostHeaderValidationMiddleware(null!, NullLogger<HostHeaderValidationMiddleware>.Instance, configuration)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new HostHeaderValidationMiddleware(next, null!, configuration)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new HostHeaderValidationMiddleware(next, NullLogger<HostHeaderValidationMiddleware>.Instance, null!)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => CreateMiddleware(string.Empty, next))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task InvokeAsync_WithWildcardAllowlist_AllowsAnyHost()
    {
        var nextInvocations = 0;
        var middleware = CreateMiddleware("*", _ =>
        {
            nextInvocations++;
            return Task.CompletedTask;
        });
        var context = CreateContext("unlisted.example.test", "/api/query");

        await middleware.InvokeAsync(context);

        nextInvocations.Should().Be(1);
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_WithHealthPath_BypassesHostValidation()
    {
        var nextInvocations = 0;
        var middleware = CreateMiddleware("api.example.com", _ =>
        {
            nextInvocations++;
            return Task.CompletedTask;
        });
        var context = CreateContext(host: null, path: "/health/ready");

        await middleware.InvokeAsync(context);

        nextInvocations.Should().Be(1);
    }

    [Theory]
    [InlineData(null, "Host header is required by HTTP protocol specification")]
    [InlineData("", "Host header cannot be empty")]
    [InlineData("evil.example.test", "Invalid Host header")]
    [InlineData("[broken", "Invalid Host header")]
    [InlineData("api.example.com:not-a-port", "Invalid Host header")]
    public async Task InvokeAsync_WithMalformedOrDisallowedHost_ReturnsSanitizedBadRequest(
        string? host,
        string expectedDetail)
    {
        var nextInvocations = 0;
        var middleware = CreateMiddleware("api.example.com", _ =>
        {
            nextInvocations++;
            return Task.CompletedTask;
        });
        var context = CreateContext(host, "/api/query");

        await middleware.InvokeAsync(context);

        nextInvocations.Should().Be(0);
        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        context.Response.ContentType.Should().Be("application/problem+json");
        (await ReadResponseAsync(context)).Should().Contain(expectedDetail);
    }

    [Theory]
    [InlineData("API.EXAMPLE.COM:8443")]
    [InlineData("[::1]:8080")]
    public async Task InvokeAsync_WithAllowedHostnameOrIpv6Host_InvokesNext(string host)
    {
        var nextInvocations = 0;
        var middleware = CreateMiddleware("api.example.com;[::1]", _ =>
        {
            nextInvocations++;
            return Task.CompletedTask;
        });
        var context = CreateContext(host, "/api/query");

        await middleware.InvokeAsync(context);

        nextInvocations.Should().Be(1);
    }

    [Fact]
    public void UseHostHeaderValidation_AddsMiddlewareToApplicationBuilder()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);

        var returnedBuilder = HostHeaderValidationMiddlewareExtensions.UseHostHeaderValidation(builder);

        returnedBuilder.Should().BeSameAs(builder);
    }

    private static HostHeaderValidationMiddleware CreateMiddleware(string allowedHosts, RequestDelegate next) =>
        new(next, NullLogger<HostHeaderValidationMiddleware>.Instance, CreateConfiguration(allowedHosts));

    private static IConfiguration CreateConfiguration(string? allowedHosts) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = allowedHosts,
        }).Build();

    private static DefaultHttpContext CreateContext(string? host, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (host is not null)
        {
            context.Request.Headers["Host"] = host;
        }
        return context;
    }

    private static async Task<string> ReadResponseAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
