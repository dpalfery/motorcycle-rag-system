using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRag.WebUI.BFF.Middleware;

namespace MotorcycleRag.WebUI.BFF.Tests.Middleware;

public class HostHeaderValidationMiddlewareTests {
    [Fact]
    public void Constructor_WithNullDependencies_ThrowsArgumentNullException() {
        var configuration = new ConfigurationBuilder().Build();
        var logger = NullLogger<HostHeaderValidationMiddleware>.Instance;
        RequestDelegate next = _ => Task.CompletedTask;

        var nullNext = () => new HostHeaderValidationMiddleware(null!, logger, configuration);
        var nullLogger = () => new HostHeaderValidationMiddleware(next, null!, configuration);
        var nullConfig = () => new HostHeaderValidationMiddleware(next, logger, null!);

        nullNext.Should().Throw<ArgumentNullException>();
        nullLogger.Should().Throw<ArgumentNullException>();
        nullConfig.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(",")]
    [InlineData(";;")]
    public void Constructor_WithEmptyAllowedHosts_ThrowsInvalidOperationException(string allowedHosts) {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["AllowedHosts"] = allowedHosts
            })
            .Build();

        var act = () => new HostHeaderValidationMiddleware(
            _ => Task.CompletedTask,
            NullLogger<HostHeaderValidationMiddleware>.Instance,
            configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AllowedHosts is empty*");
    }

    [Fact]
    public void Constructor_WithMissingAllowedHosts_DefaultsToLocalhost() {
        var middleware = new HostHeaderValidationMiddleware(
            _ => Task.CompletedTask,
            NullLogger<HostHeaderValidationMiddleware>.Instance,
            new ConfigurationBuilder().Build());

        middleware.Should().NotBeNull();
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/HEALTH/ready")]
    [InlineData("/health/live")]
    public async Task InvokeAsync_ForHealthPaths_BypassesHostValidation(string path) {
        var invoked = false;
        var middleware = CreateMiddleware("localhost", context => {
            invoked = true;
            return Task.CompletedTask;
        });
        var context = CreateContext(path: path, host: "evil.example");

        await middleware.InvokeAsync(context);

        invoked.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_WithoutHostHeader_ReturnsBadRequest() {
        var middleware = CreateMiddleware("localhost");
        var context = CreateContext(path: "/api/chat", host: null);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var body = await ReadBodyAsync(context);
        body.GetProperty("detail").GetString().Should().Contain("Host header is required");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvokeAsync_WithEmptyHostHeader_ReturnsBadRequest(string host) {
        var middleware = CreateMiddleware("localhost");
        var context = CreateContext(path: "/api/chat", host: host);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var body = await ReadBodyAsync(context);
        body.GetProperty("detail").GetString().Should().Contain("cannot be empty");
    }

    [Fact]
    public async Task InvokeAsync_WithDisallowedHost_ReturnsBadRequest() {
        var middleware = CreateMiddleware("localhost;ui.example.com");
        var context = CreateContext(path: "/api/chat", host: "evil.example");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var body = await ReadBodyAsync(context);
        body.GetProperty("detail").GetString().Should().Contain("not allowed");
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:5173")]
    [InlineData("UI.EXAMPLE.COM")]
    [InlineData("ui.example.com:443")]
    [InlineData("[::1]")]
    [InlineData("[::1]:8080")]
    [InlineData("[2001:db8::1]:443")]
    public async Task InvokeAsync_WithAllowedHost_ContinuesPipeline(string host) {
        var invoked = false;
        var middleware = CreateMiddleware("localhost;ui.example.com;[::1];[2001:db8::1]", context => {
            invoked = true;
            return Task.CompletedTask;
        });
        var context = CreateContext(path: "/api/chat", host: host);

        await middleware.InvokeAsync(context);

        invoked.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_WithAllowAll_AcceptsAnyHost() {
        var invoked = false;
        var middleware = CreateMiddleware("*", context => {
            invoked = true;
            return Task.CompletedTask;
        });
        var context = CreateContext(path: "/api/chat", host: "anything.example");

        await middleware.InvokeAsync(context);

        invoked.Should().BeTrue();
    }

    [Theory]
    [InlineData("[")]
    [InlineData("[]")]
    public async Task InvokeAsync_WithMalformedIpv6Host_ReturnsBadRequest(string host) {
        var middleware = CreateMiddleware("[::1]");
        var context = CreateContext(path: "/api/chat", host: host);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task InvokeAsync_WithNonNumericPortSuffix_TreatsFullValueAsHostname() {
        var invoked = false;
        var middleware = CreateMiddleware("HOST:NAME", context => {
            invoked = true;
            return Task.CompletedTask;
        });
        var context = CreateContext(path: "/api/chat", host: "host:name");

        await middleware.InvokeAsync(context);

        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WithNullContext_ThrowsArgumentNullException() {
        var middleware = CreateMiddleware("localhost");

        var act = async () => await middleware.InvokeAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_WithControlCharactersInHealthPath_StillBypasses() {
        var invoked = false;
        var middleware = CreateMiddleware("*", context => {
            invoked = true;
            return Task.CompletedTask;
        });
        var context = CreateContext(path: "/health\ninjection", host: "localhost");

        await middleware.InvokeAsync(context);

        invoked.Should().BeTrue();
    }

    [Fact]
    public async Task UseHostHeaderValidation_RegistersMiddlewareInPipeline() {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder => {
                webBuilder.UseTestServer();
                webBuilder.ConfigureAppConfiguration((_, config) => {
                    config.AddInMemoryCollection(new Dictionary<string, string?> {
                        ["AllowedHosts"] = "localhost"
                    });
                });
                webBuilder.ConfigureServices(services => services.AddRouting());
                webBuilder.Configure(app => {
                    app.UseHostHeaderValidation();
                    app.Run(_ => Task.CompletedTask);
                });
            })
            .StartAsync();

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Host = "evil.example";

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    private static HostHeaderValidationMiddleware CreateMiddleware(
        string allowedHosts,
        RequestDelegate? next = null) {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["AllowedHosts"] = allowedHosts
            })
            .Build();

        return new HostHeaderValidationMiddleware(
            next ?? (_ => Task.CompletedTask),
            NullLogger<HostHeaderValidationMiddleware>.Instance,
            configuration);
    }

    private static DefaultHttpContext CreateContext(string path, string? host) {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (host is not null) {
            context.Request.Headers.Host = host;
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpContext context) {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.Clone();
    }
}
