using FluentAssertions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MotorcycleRAG.API.Configuration.Services;
using MotorcycleRAG.Core.Options;
using Xunit;

namespace MotorcycleRAG.UnitTests.Presentation.API.Configuration.Services;

public class TelemetryServiceConfigurationTests
{
    private static IServiceCollection CreateServicesWithHostingEnvironment()
    {
        var services = new ServiceCollection();
        var env = new FakeWebHostEnvironment();
        services.AddSingleton<IWebHostEnvironment>(env);
        services.AddSingleton<IHostEnvironment>(env);
#pragma warning disable CS0618 // Application Insights still resolves the legacy hosting environment
        services.AddSingleton<Microsoft.AspNetCore.Hosting.IHostingEnvironment>(env);
#pragma warning restore CS0618
        services.AddLogging();
        return services;
    }

    [Fact]
    public void AddMotorcycleRagTelemetry_EnabledWithoutConnectionString_Throws()
    {
        var services = new ServiceCollection();
        var options = new TelemetryOptions { EnableTelemetry = true, ConnectionString = "" };

        var act = () => services.AddMotorcycleRagTelemetry(options);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*EnableTelemetry=true*ConnectionString*");
    }

    [Fact]
    public void AddMotorcycleRagTelemetry_EnabledWithWhitespaceConnectionString_Throws()
    {
        var services = new ServiceCollection();
        var options = new TelemetryOptions { EnableTelemetry = true, ConnectionString = "   " };

        var act = () => services.AddMotorcycleRagTelemetry(options);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddMotorcycleRagTelemetry_DisabledWithoutConnectionString_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var options = new TelemetryOptions { EnableTelemetry = false, ConnectionString = "" };

        var act = () => services.AddMotorcycleRagTelemetry(options);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddMotorcycleRagTelemetry_ReturnsSameServiceCollection_ForFluentChaining()
    {
        var services = new ServiceCollection();
        var options = new TelemetryOptions { EnableTelemetry = false, ConnectionString = "" };

        var result = services.AddMotorcycleRagTelemetry(options);

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddMotorcycleRagTelemetry_WithoutConnectionString_RegistersDisabledTelemetryClient()
    {
        var services = new ServiceCollection();
        var options = new TelemetryOptions { EnableTelemetry = false, ConnectionString = "" };

        services.AddMotorcycleRagTelemetry(options);
        using var provider = services.BuildServiceProvider();

        var config = provider.GetRequiredService<TelemetryConfiguration>();
        config.DisableTelemetry.Should().BeTrue();

        var client = provider.GetRequiredService<TelemetryClient>();
        client.Should().NotBeNull();
    }

    [Fact]
    public void AddMotorcycleRagTelemetry_WithoutConnectionString_DoesNotRegisterApplicationInsightsServiceOptions()
    {
        var services = new ServiceCollection();
        var options = new TelemetryOptions { EnableTelemetry = false, ConnectionString = "" };

        services.AddMotorcycleRagTelemetry(options);

        services.Should().NotContain(
            descriptor => descriptor.ServiceType == typeof(IConfigureOptions<ApplicationInsightsServiceOptions>));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddMotorcycleRagTelemetry_WithConnectionString_ConfiguresApplicationInsightsOptions(
        bool enablePerformanceCounters)
    {
        const string connectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
        var services = CreateServicesWithHostingEnvironment();
        var options = new TelemetryOptions
        {
            EnableTelemetry = true,
            ConnectionString = connectionString,
            EnablePerformanceCounters = enablePerformanceCounters
        };

        services.AddMotorcycleRagTelemetry(options);
        using var provider = services.BuildServiceProvider();

        var aiOptions = provider.GetRequiredService<IOptions<ApplicationInsightsServiceOptions>>().Value;

        aiOptions.ConnectionString.Should().Be(connectionString);
        aiOptions.EnableQuickPulseMetricStream.Should().BeTrue();
        aiOptions.EnablePerformanceCounterCollectionModule.Should().Be(enablePerformanceCounters);
        aiOptions.ApplicationVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AddMotorcycleRagTelemetry_WithConnectionString_RegistersTelemetryClient()
    {
        const string connectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
        var services = CreateServicesWithHostingEnvironment();
        var options = new TelemetryOptions
        {
            EnableTelemetry = true,
            ConnectionString = connectionString
        };

        services.AddMotorcycleRagTelemetry(options);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TelemetryClient>().Should().NotBeNull();
    }

    [Fact]
    public void AddMotorcycleRagTelemetry_DisabledButConnectionStringProvided_StillConfiguresApplicationInsights()
    {
        // EnableTelemetry only gates the fail-fast check; a provided ConnectionString
        // always drives the AddApplicationInsightsTelemetry branch.
        const string connectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
        var services = CreateServicesWithHostingEnvironment();
        var options = new TelemetryOptions
        {
            EnableTelemetry = false,
            ConnectionString = connectionString
        };

        services.AddMotorcycleRagTelemetry(options);
        using var provider = services.BuildServiceProvider();

        var aiOptions = provider.GetRequiredService<IOptions<ApplicationInsightsServiceOptions>>().Value;
        aiOptions.ConnectionString.Should().Be(connectionString);
    }

#pragma warning disable CS0618
    private sealed class FakeWebHostEnvironment : IWebHostEnvironment, Microsoft.AspNetCore.Hosting.IHostingEnvironment
    {
#pragma warning restore CS0618
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
