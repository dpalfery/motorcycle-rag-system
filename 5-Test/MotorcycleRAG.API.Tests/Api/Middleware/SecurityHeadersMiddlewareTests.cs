using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.API.Middleware;

namespace MotorcycleRAG.API.Tests.Api.Middleware;

public class SecurityHeadersMiddlewareTests
{
    private static SecurityHeadersMiddleware CreateMiddleware(IHostEnvironment env, RequestDelegate? next = null)
        => new(next ?? TestHelpers.NoopNext, NullLogger<SecurityHeadersMiddleware>.Instance, env);

    private static IHostEnvironment DevEnv() => new TestHostEnvironment { EnvironmentName = "Development" };
    private static IHostEnvironment ProdEnv() => new TestHostEnvironment { EnvironmentName = "Production" };

    [Fact]
    public void Constructor_NullArgs_Throw()
    {
        var env = ProdEnv();
        ((Action)(() => new SecurityHeadersMiddleware(null!, NullLogger<SecurityHeadersMiddleware>.Instance, env))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new SecurityHeadersMiddleware(TestHelpers.NoopNext, null!, env))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new SecurityHeadersMiddleware(TestHelpers.NoopNext, NullLogger<SecurityHeadersMiddleware>.Instance, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_NullContext_Throws()
    {
        var middleware = CreateMiddleware(ProdEnv());
        await ((Func<Task>)(async () => await middleware.InvokeAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_AddsBaselineSecurityHeaders()
    {
        var middleware = CreateMiddleware(ProdEnv(), TestHelpers.ContinueNext);
        var context = TestHelpers.CreateContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers.XContentTypeOptions.ToString().Should().Be("nosniff");
        context.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
        context.Response.Headers.ContentSecurityPolicy.ToString().Should().Contain("default-src 'none'");
        context.Response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
        context.Response.Headers["Permissions-Policy"].ToString().Should().Contain("geolocation=()");
    }

    [Fact]
    public async Task InvokeAsync_ProductionHttps_AddsHsts()
    {
        var middleware = CreateMiddleware(ProdEnv(), TestHelpers.ContinueNext);
        var context = TestHelpers.CreateContext();
        context.Request.Scheme = "https";

        await middleware.InvokeAsync(context);

        context.Response.Headers["Strict-Transport-Security"].ToString().Should().Contain("max-age=63072000");
        context.Response.StatusCode.Should().Be(299);
    }

    [Fact]
    public async Task InvokeAsync_ProductionHttp_NoHsts()
    {
        var middleware = CreateMiddleware(ProdEnv(), TestHelpers.ContinueNext);
        var context = TestHelpers.CreateContext();
        context.Request.Scheme = "http";

        await middleware.InvokeAsync(context);

        context.Response.Headers.ContainsKey("Strict-Transport-Security").Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_DevelopmentHttps_NoHsts()
    {
        var middleware = CreateMiddleware(DevEnv(), TestHelpers.ContinueNext);
        var context = TestHelpers.CreateContext();
        context.Request.Scheme = "https";

        await middleware.InvokeAsync(context);

        context.Response.Headers.ContainsKey("Strict-Transport-Security").Should().BeFalse();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
