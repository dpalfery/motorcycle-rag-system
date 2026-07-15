using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.API.Extensions;
using MotorcycleRAG.API.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;

namespace MotorcycleRAG.UnitTests.Presentation.API.Extensions;

public sealed class RegistrationExtensionsCoverageTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void AddStructuredLogging_ConfiguresDevelopmentAndProductionProviders(string environmentName)
    {
        var services = new ServiceCollection();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns(environmentName);
        services.AddLogging(builder =>
        {
            builder.AddStructuredLogging(environment.Object).Should().BeSameAs(builder);
        });
    }

    [Fact]
    public void MiddlewareRegistrationExtensions_ReturnTheirApplicationBuilder()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(provider);

        AuthorizationMiddlewareExtensions.UseAuthorizationLogging(builder).Should().BeSameAs(builder);
        CorrelationIdMiddlewareExtensions.UseCorrelationId(builder).Should().BeSameAs(builder);
        ExceptionHandlingMiddlewareExtensions.UseExceptionHandling(builder).Should().BeSameAs(builder);
        HostHeaderValidationMiddlewareExtensions.UseHostHeaderValidation(builder).Should().BeSameAs(builder);
        RequestPipelineTimingMiddlewareExtensions.UseRequestPipelineTiming(builder).Should().BeSameAs(builder);
        SecurityHeadersMiddlewareExtensions.UseSecurityHeaders(builder).Should().BeSameAs(builder);
    }

    [Fact]
    public void UseMotorcycleRagMiddleware_ComposesProductionPipeline()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*", ["AppConfig:Enabled"] = "false"
        });
        builder.Services.AddControllers();
        builder.Services.AddCors();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(_ => { });
        builder.Services.AddAntiforgery();
        builder.Services.AddHealthChecks();
        using var app = builder.Build();

        app.UseMotorcycleRagMiddleware().Should().BeSameAs(app);
    }

    [Fact]
    public void UseMotorcycleRagMiddleware_ComposesDevelopmentDocumentationPipeline()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["AppConfig:Enabled"] = "false"
        });
        builder.Services.AddControllers();
        builder.Services.AddCors();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(_ => { });
        builder.Services.AddAntiforgery();
        builder.Services.AddHealthChecks();
        using var app = builder.Build();

        app.UseMotorcycleRagMiddleware().Should().BeSameAs(app);
    }
}
