using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.ExternalServices;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Telemetry;

namespace MotorcycleRAG.IntegrationTests.Configuration;

/// <summary>
/// Validates the API's real composition root using in-memory configuration only.
/// This fixture deliberately resolves registrations without invoking Azure, Graph,
/// local processor, or SQL operations.
/// </summary>
public class ApiCompositionContainerValidationTests
{
    [Theory]
    [InlineData(typeof(IAuditService), typeof(AuditService), ServiceLifetime.Scoped)]
    [InlineData(typeof(ILocalPipelineService), typeof(LocalPipelineService), ServiceLifetime.Scoped)]
    [InlineData(typeof(IIngestionTelemetryService), typeof(IngestionTelemetryService), ServiceLifetime.Singleton)]
    public void ApiComposition_RegistersRemediationServicesExactlyOnce(
        Type serviceType,
        Type implementationType,
        ServiceLifetime expectedLifetime)
    {
        // Act
        var registrations = ApiCompositionTestHarness.CreateServices()
            .Where(descriptor => descriptor.ServiceType == serviceType)
            .ToArray();

        // Assert
        registrations.Should().ContainSingle();
        registrations[0].ImplementationType.Should().Be(implementationType);
        registrations[0].Lifetime.Should().Be(expectedLifetime);
    }

    [Fact]
    public void ApiComposition_ResolvesRemediationServicesWithTheirDeclaredLifetimes()
    {
        // Arrange
        using var serviceProvider = ApiCompositionTestHarness.CreateServices()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var firstScope = serviceProvider.CreateScope();
        using var secondScope = serviceProvider.CreateScope();

        // Act
        var firstAuditService = firstScope.ServiceProvider.GetRequiredService<IAuditService>();
        var secondAuditService = secondScope.ServiceProvider.GetRequiredService<IAuditService>();
        var firstLocalPipelineService = firstScope.ServiceProvider.GetRequiredService<ILocalPipelineService>();
        var secondLocalPipelineService = secondScope.ServiceProvider.GetRequiredService<ILocalPipelineService>();
        var firstTelemetryService = firstScope.ServiceProvider.GetRequiredService<IIngestionTelemetryService>();
        var secondTelemetryService = secondScope.ServiceProvider.GetRequiredService<IIngestionTelemetryService>();

        // Assert
        firstAuditService.Should().BeOfType<AuditService>();
        secondAuditService.Should().BeOfType<AuditService>();
        firstAuditService.Should().NotBeSameAs(secondAuditService);

        firstLocalPipelineService.Should().BeOfType<LocalPipelineService>();
        secondLocalPipelineService.Should().BeOfType<LocalPipelineService>();
        firstLocalPipelineService.Should().NotBeSameAs(secondLocalPipelineService);

        firstTelemetryService.Should().BeOfType<IngestionTelemetryService>();
        secondTelemetryService.Should().BeOfType<IngestionTelemetryService>();
        firstTelemetryService.Should().BeSameAs(secondTelemetryService);
    }

    [Fact]
    public void ApiComposition_OverridesBothAppConfigurationInputsWithoutStartingAHost()
    {
        // Act
        var services = ApiCompositionTestHarness.CreateServices();
        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();

        // Assert
        configuration["AppConfig:Endpoint"].Should().BeEmpty();
        configuration["AppConfig:ConnectionString"].Should().BeEmpty();
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));
    }

    [Fact]
    public void ApiComposition_RegistersEachPersistenceContractExactlyOnce()
    {
        var services = ApiCompositionTestHarness.CreateServices();
        var contractTypes = new[]
        {
            typeof(IConfigureOptions<SqlOptions>),
            typeof(IValidateOptions<SqlOptions>),
            typeof(ISqlConnectionFactory),
            typeof(IIngestionJobRepository),
            typeof(IGraphRepository),
            typeof(IBikeModelRepository),
            typeof(IBikeModelCategoryRepository),
            typeof(IAuditRepository),
            typeof(IUserRepository),
            typeof(IUsageRepository),
            typeof(IPlanRepository),
            typeof(IWebSourceRepository),
            typeof(IManualDocumentRepository),
            typeof(IAccessRequestRepository),
            typeof(IUserIdentityRepository),
            typeof(IUserManagementQueryRepository),
            typeof(IWebScrapeRunRepository),
            typeof(IIndexedArtifactRepository),
            typeof(IIndexedChunkRepository),
            typeof(IToolConfigurationRepository),
            typeof(IToolConfigurationAuditRepository),
            typeof(IWebTrustPolicyStore),
            typeof(ITelemetryService),
            typeof(ICorrelationService)
        };

        foreach (var contractType in contractTypes)
        {
            services.Where(descriptor => descriptor.ServiceType == contractType)
                .Should().ContainSingle($"{contractType.Name} has one Persistence-owned registration");
        }
    }

    [Fact]
    public void ApiComposition_UsesTheSameScheduledPipelineInstanceForServiceAndHost()
    {
        using var provider = ApiCompositionTestHarness.CreateServices()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var scheduledPipeline = provider.GetRequiredService<IScheduledPipelineService>();
        var hostedPipeline = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<MotorcycleRAG.Application.Services.Ingestion.ScheduledPipelineService>()
            .Single();

        scheduledPipeline.Should().BeSameAs(hostedPipeline);
    }
}
