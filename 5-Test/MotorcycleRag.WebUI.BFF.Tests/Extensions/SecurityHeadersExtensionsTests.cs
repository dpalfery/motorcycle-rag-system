using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Moq;
using MotorcycleRag.WebUI.BFF.Extensions;
using Xunit;

namespace MotorcycleRag.WebUI.BFF.Tests.Extensions;

/// <summary>
/// Tests that UseSecurityHeaders injects the correct response headers.
/// Uses a minimal in-process TestServer to invoke the middleware and inspect headers.
/// </summary>
public class SecurityHeadersExtensionsTests
{
    private static async Task<HttpResponseMessage> GetResponseAsync(string envName = "Production")
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(envName);
        env.Setup(e => e.ApplicationName).Returns("TestApp");
        env.Setup(e => e.ContentRootPath).Returns(AppContext.BaseDirectory);
        env.Setup(e => e.WebRootPath).Returns(AppContext.BaseDirectory);

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.Configure(app =>
                {
                    app.UseSecurityHeaders(env.Object);
                    app.Run(ctx => Task.CompletedTask);
                });
            })
            .StartAsync();

        return await host.GetTestClient().GetAsync(new Uri("/", UriKind.Relative));
    }

    [Fact]
    public async Task UseSecurityHeaders_SetsXFrameOptionsDeny()
    {
        var response = await GetResponseAsync();
        response.Headers.TryGetValues("X-Frame-Options", out var values);
        values.Should().ContainSingle().Which.Should().Be("DENY");
    }

    [Fact]
    public async Task UseSecurityHeaders_SetsXContentTypeOptionsNosniff()
    {
        var response = await GetResponseAsync();
        response.Headers.TryGetValues("X-Content-Type-Options", out var values);
        values.Should().ContainSingle().Which.Should().Be("nosniff");
    }

    [Fact]
    public async Task UseSecurityHeaders_SetsXXssProtection()
    {
        var response = await GetResponseAsync();
        response.Headers.TryGetValues("X-XSS-Protection", out var values);
        values.Should().ContainSingle().Which.Should().Be("1; mode=block");
    }

    [Fact]
    public async Task UseSecurityHeaders_SetsReferrerPolicy()
    {
        var response = await GetResponseAsync();
        response.Headers.TryGetValues("Referrer-Policy", out var values);
        values.Should().ContainSingle().Which.Should().Be("strict-origin-when-cross-origin");
    }

    [Fact]
    public async Task UseSecurityHeaders_SetsPermissionsPolicy()
    {
        var response = await GetResponseAsync();
        response.Headers.TryGetValues("Permissions-Policy", out var values);
        var policy = values.Should().ContainSingle().Subject;
        policy.Should().Contain("geolocation=()");
        policy.Should().Contain("microphone=()");
        policy.Should().Contain("camera=()");
    }

    [Fact]
    public async Task UseSecurityHeaders_Production_CspHasUpgradeInsecureRequests()
    {
        var response = await GetResponseAsync(envName: "Production");
        response.Headers.TryGetValues("Content-Security-Policy", out var values);
        values.Should().ContainSingle().Which.Should().Contain("upgrade-insecure-requests");
    }

    [Fact]
    public async Task UseSecurityHeaders_Production_CspDefaultSrcNone()
    {
        var response = await GetResponseAsync(envName: "Production");
        response.Headers.TryGetValues("Content-Security-Policy", out var values);
        values.Should().ContainSingle().Which.Should().Contain("default-src 'none'");
    }

    [Fact]
    public async Task UseSecurityHeaders_Development_CspAllowsUnsafeInlineAndEval()
    {
        var response = await GetResponseAsync(envName: "Development");
        response.Headers.TryGetValues("Content-Security-Policy", out var values);
        var csp = values.Should().ContainSingle().Subject;
        csp.Should().Contain("'unsafe-inline'");
        csp.Should().Contain("'unsafe-eval'");
        csp.Should().NotContain("upgrade-insecure-requests");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task UseSecurityHeaders_BothEnvironments_CspDeniesFrameAncestors(string envName)
    {
        var response = await GetResponseAsync(envName: envName);
        response.Headers.TryGetValues("Content-Security-Policy", out var values);
        values.Should().ContainSingle().Which.Should().Contain("frame-ancestors 'none'",
            because: $"frame-ancestors must be 'none' in {envName}");
    }
}
