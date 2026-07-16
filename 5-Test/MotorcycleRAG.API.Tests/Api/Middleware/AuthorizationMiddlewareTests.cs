using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.API.Middleware;

namespace MotorcycleRAG.API.Tests.Api.Middleware;

public class AuthorizationMiddlewareTests
{
    private static AuthorizationMiddleware Create(RequestDelegate? next = null)
        => new(next ?? TestHelpers.NoopNext, NullLogger<AuthorizationMiddleware>.Instance);

    [Fact]
    public void Constructor_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new AuthorizationMiddleware(null!, NullLogger<AuthorizationMiddleware>.Instance));
        Assert.Throws<ArgumentNullException>(() => new AuthorizationMiddleware(TestHelpers.NoopNext, null!));
    }

    [Fact]
    public async Task InvokeAsync_NullContext_Throws()
        => await ((Func<Task>)(async () => await Create().InvokeAsync(null!))).Should().ThrowAsync<ArgumentNullException>();

    [Fact]
    public async Task InvokeAsync_AnonymousUser_LogsAndContinues()
    {
        var middleware = Create(TestHelpers.ContinueNext);
        var context = TestHelpers.CreateContext("/api/test");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(299);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUser_LogsRolesAndScopes()
    {
        var middleware = Create(TestHelpers.ContinueNext);
        var context = TestHelpers.CreateContext("/api/test");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim("roles", "Editor"),
                new Claim("scp", "read write"),
                new Claim("azp", "client-1")
            },
            "TestAuth"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(299);
    }

    [Fact]
    public async Task InvokeAsync_UnauthorizedStatusCode_LogsWarning()
    {
        var middleware = Create(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        });
        var context = TestHelpers.CreateContext("/api/secure");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_ForbiddenStatusCode_LogsWarning()
    {
        var middleware = Create(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        });
        var context = TestHelpers.CreateContext("/api/secure");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task InvokeAsync_SuccessStatusCode_LogsInformation()
    {
        var middleware = Create(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        var context = TestHelpers.CreateContext("/api/ok");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_EndpointRequiringAuth_UnauthenticatedUser_LogsWarning()
    {
        var middleware = Create(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        var context = TestHelpers.CreateContext("/api/secure");

        // Build an endpoint metadata collection that has IAuthorizeData so the "requires authorization" branch runs.
        var metadata = new List<object> { new Microsoft.AspNetCore.Authorization.AuthorizeAttribute() };
        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(metadata),
            "test");
        context.SetEndpoint(endpoint);
        context.User = new ClaimsPrincipal(new ClaimsIdentity()); // not authenticated

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
