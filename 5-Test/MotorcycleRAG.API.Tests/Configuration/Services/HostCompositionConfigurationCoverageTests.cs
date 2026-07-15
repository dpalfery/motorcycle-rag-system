using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using System.Collections;
using System.Reflection;
using MotorcycleRAG.API.Configuration.Services;

namespace MotorcycleRAG.UnitTests.Presentation.API.Configuration.Services;

public sealed class HostCompositionConfigurationCoverageTests
{
    [Fact]
    public async Task RateLimitingConfiguration_RegistersAndExecutesEveryNamedPolicy()
    {
        await using var app = await BuildRateLimitedAppAsync();
        var client = app.GetTestClient();

        (await client.GetAsync(new Uri("http://localhost/public"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("http://localhost/access-requests"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("http://localhost/authenticated"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("http://localhost/ingestion-jobs"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("http://localhost/manuals-view"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RateLimitingConfiguration_AccessRequestsRejectsTheSixthRequest()
    {
        await using var app = await BuildRateLimitedAppAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("coverage", "1.0"));

        for (var request = 0; request < 5; request++)
            (await client.GetAsync(new Uri("http://localhost/access-requests"))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetAsync(new Uri("http://localhost/access-requests"))).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public void GetRateLimitForUser_NullPrincipal_ReturnsAnonymousLimit()
    {
        RateLimitingServiceConfiguration.GetRateLimitForUser(null!).Should().Be(50);
    }

    [Fact]
    public void GetRateLimitForUser_PrincipalWithoutIdentity_ReturnsAnonymousLimit()
    {
        RateLimitingServiceConfiguration.GetRateLimitForUser(new ClaimsPrincipal()).Should().Be(50);
    }

    [Fact]
    public async Task RateLimitingConfiguration_UsesRemoteIpAndAnonymousObjectIdFallbacks()
    {
        await using var remoteIpApp = await BuildRateLimitedAppAsync(remoteIpAddress: IPAddress.Loopback);
        (await remoteIpApp.GetTestClient().GetAsync(new Uri("http://localhost/access-requests"))).StatusCode
            .Should().Be(HttpStatusCode.OK);

        await using var anonymousApp = await BuildRateLimitedAppAsync(includeObjectId: false);
        var anonymousClient = anonymousApp.GetTestClient();
        (await anonymousClient.GetAsync(new Uri("http://localhost/authenticated"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymousClient.GetAsync(new Uri("http://localhost/ingestion-jobs"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymousClient.GetAsync(new Uri("http://localhost/manuals-view"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApiDocumentationConfiguration_ExposesConfiguredOpenApiDocument()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiDocumentation();
        await using var app = builder.Build();
        app.MapOpenApi();
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync(new Uri("http://localhost/openapi/v1.json"));
        var document = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.Should().Contain("Motorcycle RAG API");
        document.Should().Contain("AI-powered motorcycle information retrieval system");
    }

    [Fact]
    public async Task ApiDocumentationConfiguration_UpdatesExistingDocumentInfo()
    {
        var services = new ServiceCollection();
        services.AddApiDocumentation();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenApiOptions>>().Get("v1");
        var document = new OpenApiDocument { Info = new OpenApiInfo { Title = "previous title" } };

        var transformers = (IEnumerable)options.GetType()
            .GetField("DocumentTransformers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(options)!;
        var transformer = transformers.Cast<object>().Single();
        var transformAsync = transformer.GetType().GetMethod("TransformAsync", BindingFlags.Instance | BindingFlags.Public)!;
        await (Task)transformAsync.Invoke(transformer, [document, null!, CancellationToken.None])!;

        var documentWithoutInfo = new OpenApiDocument { Info = null! };
        await (Task)transformAsync.Invoke(transformer, [documentWithoutInfo, null!, CancellationToken.None])!;

        document.Info.Title.Should().Be("Motorcycle RAG API");
        document.Info.Version.Should().Be("v1");
        documentWithoutInfo.Info.Title.Should().Be("Motorcycle RAG API");
    }

    private static async Task<WebApplication> BuildRateLimitedAppAsync(
        bool includeObjectId = true,
        IPAddress? remoteIpAddress = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMotorcycleRagRateLimiting();
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var claims = includeObjectId ? new[] { new Claim("oid", "coverage-user") } : Array.Empty<Claim>();
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "coverage"));
            context.Connection.RemoteIpAddress = remoteIpAddress;
            await next(context);
        });
        app.UseRateLimiter();
        app.MapGet("/public", () => Results.Ok()).RequireRateLimiting("public");
        app.MapGet("/access-requests", () => Results.Ok()).RequireRateLimiting("access-requests");
        app.MapGet("/authenticated", () => Results.Ok()).RequireRateLimiting("authenticated");
        app.MapGet("/ingestion-jobs", () => Results.Ok()).RequireRateLimiting("ingestion-jobs");
        app.MapGet("/manuals-view", () => Results.Ok()).RequireRateLimiting("manuals-view");
        await app.StartAsync();
        return app;
    }
}
