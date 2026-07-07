using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Application.Features.Ingestion.Validators;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using Xunit;

namespace MotorcycleRAG.IntegrationTests.Configuration;

/// <summary>
/// Verifies that all services injected into API controllers are registered in the
/// production DI container. Uses ProductionServicesWebApplicationFactory which does NOT
/// mock core application services, so a missing registration causes the test to fail
/// rather than silently passing with a mock substitute.
///
/// Add a test here whenever a new controller dependency is introduced.
/// </summary>
public class ControllerDIRegistrationTests : IClassFixture<ProductionServicesWebApplicationFactory>
{
    private readonly ProductionServicesWebApplicationFactory _factory;

    public ControllerDIRegistrationTests(ProductionServicesWebApplicationFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    // ── MotorcycleController ─────────────────────────────────────────────────

    [Fact]
    public void IMotorcycleRagService_ShouldBeRegisteredWithProductionImplementation()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMotorcycleRagService>();
        Assert.IsType<MotorcycleRagService>(service);
    }

    [Fact]
    public void ICurrentUserService_ShouldBeRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICurrentUserService>());
    }

    [Fact]
    public void IPlanPolicyService_ShouldBeRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPlanPolicyService>());
    }

    [Fact]
    public void IUsageTrackingService_ShouldBeRegisteredWithProductionImplementation()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUsageTrackingService>();
        Assert.IsType<UsageTrackingService>(service);
    }

    // ── MeController ────────────────────────────────────────────────────────
    // ICurrentUserService, IUserRepository, IUsageTrackingService, IPlanPolicyService — covered above

    [Fact]
    public void IUserRepository_ShouldBeRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserRepository>());
    }

    // ── McpAdminController ───────────────────────────────────────────────────

    [Fact]
    public void IToolConfigurationService_ShouldBeRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IToolConfigurationService>());
    }

    // ── PlansAdminController / UsersAdminController ──────────────────────────

    [Fact]
    public void IPlanRepository_ShouldBeRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPlanRepository>());
    }

    [Fact]
    public void IUserAdminService_ShouldBeRegisteredWithProductionImplementation()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        Assert.IsType<UserAdminService>(service);
    }

    // ── WebSourcesAdminController ─────────────────────────────────────────────

    [Fact]
    public void WebSourceRegistryService_ShouldBeRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<WebSourceRegistryService>());
    }

    // ── IngestionJobsController ───────────────────────────────────────────────

    [Fact]
    public void IIngestionJobService_ShouldBeRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IIngestionJobService>());
    }

    [Fact]
    public void IngestionJobValidator_ShouldBeRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IngestionJobValidator>());
    }

    [Fact]
    public void IBlobStorageService_ShouldBeRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IBlobStorageService>());
    }

    // ── ManualsController ─────────────────────────────────────────────────────

    [Fact]
    public void IManualPageQueryService_ShouldBeRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IManualPageQueryService>());
    }

    // ── DataPipelineUploadController / FileUploadController ──────────────────

    [Fact]
    public void IFileUploadService_ShouldBeRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IFileUploadService>());
    }

    // ── DataPipelineProcessingController / PipelineProcessingController / FileUploadController

    [Fact]
    public void IDataPipelineOrchestrator_ShouldBeRegistered()
    {
        using var scope = _factory.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDataPipelineOrchestrator>());
    }

    // ── PipelineMonitoringController ──────────────────────────────────────────

    [Fact]
    public void IPipelineMonitoringService_ShouldBeRegistered()
    {
        Assert.NotNull(_factory.Services.GetRequiredService<IPipelineMonitoringService>());
    }

    // ── ScheduledProcessingController ─────────────────────────────────────────

    [Fact]
    public void IScheduledPipelineService_ShouldBeRegistered()
    {
        Assert.NotNull(_factory.Services.GetRequiredService<IScheduledPipelineService>());
    }
}
