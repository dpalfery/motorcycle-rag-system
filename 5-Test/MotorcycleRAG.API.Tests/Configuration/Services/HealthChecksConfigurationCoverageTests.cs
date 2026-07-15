using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Configuration.Services;

namespace MotorcycleRAG.UnitTests.Presentation.API.Configuration.Services;

public sealed class HealthChecksConfigurationCoverageTests
{
    [Theory]
    [InlineData(null, null, true, false)]
    [InlineData("InMemoryShim", "https://document-intelligence.example.test", false, true)]
    public void AddHealthChecks_RegistersOnlyConfiguredExternalDependencyChecks(
        string? chunkIndexingProvider,
        string? documentIntelligenceEndpoint,
        bool expectSearch,
        bool expectDocumentIntelligence)
    {
        using var provider = BuildProvider(CreateConfiguration(chunkIndexingProvider, documentIntelligenceEndpoint));
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        var names = registrations.Select(registration => registration.Name);

        names.Should().Contain(["self", "azure_openai", "azure_foundry", "sql_database", "configuration", "onboarding_notification", "external_identity_provisioning"]);
        if (expectSearch)
            names.Should().Contain("azure_search");
        else
            names.Should().NotContain("azure_search");

        if (expectDocumentIntelligence)
            names.Should().Contain("azure_document_intelligence");
        else
            names.Should().NotContain("azure_document_intelligence");
    }

    [Fact]
    public async Task ConfigurationHealthCheck_WithCompleteConfiguration_IsHealthy()
    {
        using var provider = BuildProvider(CreateConfiguration());

        var result = await RunCheckAsync(provider, "configuration");

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task ConfigurationHealthCheck_WithMissingConfiguration_IsDegraded()
    {
        using var provider = BuildProvider(new ConfigurationBuilder().Build());

        var result = await RunCheckAsync(provider, "configuration");

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("AzureAI configuration section is missing");
    }

    [Fact]
    public async Task ConfigurationHealthCheck_WhenConfigurationAccessFails_IsUnhealthy()
    {
        var configuration = new Mock<IConfiguration>(MockBehavior.Strict);
        configuration.SetupGet(item => item[It.IsAny<string>()]).Returns((string?)null);
        configuration.Setup(item => item.GetSection(It.IsAny<string>())).Throws<InvalidOperationException>();
        using var provider = BuildProvider(configuration.Object);

        var result = await RunCheckAsync(provider, "configuration");

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task OnboardingHealthCheck_WithAndWithoutApproverAddress_ReturnsExpectedStatus()
    {
        using var healthyProvider = BuildProvider(CreateConfiguration());
        using var degradedProvider = BuildProvider(CreateConfiguration(approverAddress: null));

        (await RunCheckAsync(healthyProvider, "onboarding_notification")).Status.Should().Be(HealthStatus.Healthy);
        (await RunCheckAsync(degradedProvider, "onboarding_notification")).Status.Should().Be(HealthStatus.Degraded);
    }

    [Theory]
    [InlineData("https://admin.example.test/complete", "client-id", null, HealthStatus.Healthy)]
    [InlineData("https://admin.example.test/complete", null, "object-id", HealthStatus.Healthy)]
    [InlineData(null, null, null, HealthStatus.Degraded)]
    public async Task ExternalIdentityHealthCheck_ValidatesRedirectAndApplicationIdentifier(
        string? redirectUrl,
        string? clientId,
        string? servicePrincipalObjectId,
        HealthStatus expectedStatus)
    {
        using var provider = BuildProvider(CreateConfiguration(
            inviteRedirectUrl: redirectUrl,
            apiApplicationClientId: clientId,
            apiServicePrincipalObjectId: servicePrincipalObjectId));

        var result = await RunCheckAsync(provider, "external_identity_provisioning");

        result.Status.Should().Be(expectedStatus);
    }

    private static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddHealthChecks(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task<HealthCheckResult> RunCheckAsync(ServiceProvider provider, string name)
    {
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Single(item => item.Name == name);
        var check = registration.Factory(provider);
        return await check.CheckHealthAsync(new HealthCheckContext());
    }

    private static IConfiguration CreateConfiguration(
        string? chunkIndexingProvider = null,
        string? documentIntelligenceEndpoint = null,
        string? approverAddress = "approver@example.test",
        string? inviteRedirectUrl = "https://admin.example.test/complete",
        string? apiApplicationClientId = "client-id",
        string? apiServicePrincipalObjectId = null)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAI:FoundryEndpoint"] = "https://foundry.example.test",
            ["Search:IndexName"] = "manuals",
            ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=00000000-0000-0000-0000-000000000000",
            ["Search:ChunkIndexingProvider"] = chunkIndexingProvider,
            ["AzureAI:DocumentIntelligenceEndpoint"] = documentIntelligenceEndpoint,
            ["Onboarding:ApproverAddress"] = approverAddress,
            ["ExternalIdentityProvisioning:InviteRedirectUrl"] = inviteRedirectUrl,
            ["ExternalIdentityProvisioning:ApiApplicationClientId"] = apiApplicationClientId,
            ["ExternalIdentityProvisioning:ApiServicePrincipalObjectId"] = apiServicePrincipalObjectId
        }).Build();
    }
}
