using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Persistence.Sql.Repositories;
using MotorcycleRAG.Persistence.Configuration;
using Xunit;

namespace MotorcycleRAG.IntegrationTests.Configuration;

/// <summary>
/// Integration tests for Dependency Injection container registration
/// Verifies that all critical services are properly registered and can be resolved
/// </summary>
public class DIRegistrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public DIRegistrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    [Fact]
    public void IWebScrapeRunRepository_ShouldBeRegisteredAsScoped()
    {
        // Arrange & Act
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetService<IWebScrapeRunRepository>();

        // Assert
        Assert.NotNull(service);
        Assert.IsType<WebScrapeRunRepository>(service);
    }

    [Fact]
    public void IWebScrapeOrchestrator_ShouldBeRegisteredAsScoped()
    {
        // Arrange & Act
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetService<IWebScrapeOrchestrator>();

        // Assert
        Assert.NotNull(service);
        Assert.IsType<WebScrapeOrchestrator>(service);
    }

    [Fact]
    public void IWebTrustPolicyStore_ShouldBeRegisteredAsSingleton()
    {
        // Arrange & Act
        var service1 = _factory.Services.GetService<IWebTrustPolicyStore>();
        var service2 = _factory.Services.GetService<IWebTrustPolicyStore>();

        // Assert
        Assert.NotNull(service1);
        Assert.NotNull(service2);
        Assert.IsType<WebTrustPolicyStore>(service1);
        // Verify singleton behavior - same instance
        Assert.Same(service1, service2);
    }

    [Fact]
    public void WebSourceRegistryService_ShouldBeRegisteredAsScoped()
    {
        // Arrange & Act
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetService<WebSourceRegistryService>();

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void IWebScrapeRunRepository_ShouldNotBeSingleton()
    {
        // Arrange & Act
        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();
        
        var service1 = scope1.ServiceProvider.GetService<IWebScrapeRunRepository>();
        var service2 = scope2.ServiceProvider.GetService<IWebScrapeRunRepository>();

        // Assert - should be different instances (scoped, not singleton)
        Assert.NotNull(service1);
        Assert.NotNull(service2);
        Assert.NotSame(service1, service2);
    }

    [Fact]
    public void IWebScrapeOrchestrator_ShouldNotBeSingleton()
    {
        // Arrange & Act
        using var scope1 = _factory.Services.CreateScope();
        using var scope2 = _factory.Services.CreateScope();
        
        var service1 = scope1.ServiceProvider.GetService<IWebScrapeOrchestrator>();
        var service2 = scope2.ServiceProvider.GetService<IWebScrapeOrchestrator>();

        // Assert - should be different instances (scoped, not singleton)
        Assert.NotNull(service1);
        Assert.NotNull(service2);
        Assert.NotSame(service1, service2);
    }

    [Fact]
    public void AllRegisteredServices_ShouldBeResolvable()
    {
        // Arrange & Act
        using var scope = _factory.Services.CreateScope();
        
        var webScrapeRunRepository = scope.ServiceProvider.GetService<IWebScrapeRunRepository>();
        var webScrapeOrchestrator = scope.ServiceProvider.GetService<IWebScrapeOrchestrator>();
        var webTrustPolicyStore = _factory.Services.GetService<IWebTrustPolicyStore>();
        var webSourceRegistry = scope.ServiceProvider.GetService<WebSourceRegistryService>();

        // Assert - all services should resolve successfully
        Assert.NotNull(webScrapeRunRepository);
        Assert.NotNull(webScrapeOrchestrator);
        Assert.NotNull(webTrustPolicyStore);
        Assert.NotNull(webSourceRegistry);
    }
}
