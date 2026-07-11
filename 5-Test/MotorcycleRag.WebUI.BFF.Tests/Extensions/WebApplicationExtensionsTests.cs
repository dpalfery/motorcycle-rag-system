using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MotorcycleRag.WebUI.BFF.Tests.DataProtection;

namespace MotorcycleRag.WebUI.BFF.Tests.Extensions;

public class WebApplicationExtensionsTests {
    [Fact]
    public async Task UseMotorcycleRagBffMiddleware_MapsHealthEndpoint() {
        using var factory = new DataProtectionTestWebApplicationFactory(
            blobUri: (Uri?)null,
            environment: "Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UseMotorcycleRagBffMiddleware_RejectsDisallowedHostOutsideHealth() {
        using var factory = new HostValidationWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Host = "evil.example";

        var response = await client.GetAsync(new Uri("/auth/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UseMotorcycleRagBffMiddleware_InStagingWithBlobAndTelemetry_Starts() {
        using var factory = new StagingBlobTelemetryWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class HostValidationWebApplicationFactory : DataProtectionTestWebApplicationFactory {
        public HostValidationWebApplicationFactory()
            : base(blobUri: (Uri?)null, environment: "Development") {
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => {
                config.AddInMemoryCollection(new Dictionary<string, string?> {
                    ["AllowedHosts"] = "localhost;127.0.0.1"
                });
            });
        }
    }

    private sealed class StagingBlobTelemetryWebApplicationFactory : DataProtectionTestWebApplicationFactory {
        public StagingBlobTelemetryWebApplicationFactory()
            : base(
                blobUri: new Uri("https://teststorage.blob.core.windows.net/testcontainer/keys.xml"),
                environment: "Staging") {
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => {
                config.AddInMemoryCollection(new Dictionary<string, string?> {
                    ["ConnectionStrings:ApplicationInsights"] =
                        "InstrumentationKey=00000000-0000-0000-0000-000000000000",
                    ["ApplicationInsights:ConnectionString"] =
                        "InstrumentationKey=00000000-0000-0000-0000-000000000000"
                });
            });
        }
    }
}
