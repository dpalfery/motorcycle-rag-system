using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MotorcycleRag.WebUI.BFF.Configuration.Services;

namespace MotorcycleRag.WebUI.BFF.Tests.Configuration.Services;

public class TelemetryServiceConfigurationTests {
    [Fact]
    public void AddBffTelemetry_WithoutConnectionString_DoesNotRegisterTelemetryClient() {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var result = services.AddBffTelemetry(configuration);

        result.Should().BeSameAs(services);
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(TelemetryClient));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddBffTelemetry_WithConnectionString_RegistersConfiguredTelemetry(bool useConnectionStringsSection) {
        const string connectionString =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000";
        var key = useConnectionStringsSection
            ? "ConnectionStrings:ApplicationInsights"
            : "ApplicationInsights:ConnectionString";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                [key] = connectionString
            })
            .Build();
        var services = new ServiceCollection();
        AddHostingEnvironment(services);

        services.AddBffTelemetry(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TelemetryClient>().Should().NotBeNull();
        var options = provider.GetRequiredService<IOptions<ApplicationInsightsServiceOptions>>().Value;
        options.ConnectionString.Should().Be(connectionString);
        options.EnableQuickPulseMetricStream.Should().BeTrue();
    }

    private static void AddHostingEnvironment(IServiceCollection services) {
        var env = new FakeWebHostEnvironment();
        services.AddSingleton<IWebHostEnvironment>(env);
        services.AddSingleton<IHostEnvironment>(env);
#pragma warning disable CS0618 // Application Insights still resolves the legacy hosting environment
        services.AddSingleton<Microsoft.AspNetCore.Hosting.IHostingEnvironment>(env);
#pragma warning restore CS0618
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    }

#pragma warning disable CS0618
    private sealed class FakeWebHostEnvironment : IWebHostEnvironment, Microsoft.AspNetCore.Hosting.IHostingEnvironment {
#pragma warning restore CS0618
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
