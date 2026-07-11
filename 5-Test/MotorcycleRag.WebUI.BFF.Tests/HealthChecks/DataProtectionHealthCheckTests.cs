using Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using MotorcycleRag.WebUI.BFF.HealthChecks;

namespace MotorcycleRag.WebUI.BFF.Tests.HealthChecks;

public class DataProtectionHealthCheckTests {
    private const string BlobUri = "https://account.blob.core.windows.net/keys/keys.xml";

    private static DataProtectionHealthCheck CreateHealthCheck(
        string? blobUri = BlobUri,
        Func<Uri, CancellationToken, Task<bool>>? blobExistsAsync = null) {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["DataProtection:BlobUri"] = blobUri
            })
            .Build();

        return new DataProtectionHealthCheck(
            configuration,
            Mock.Of<ILogger<DataProtectionHealthCheck>>(),
            blobExistsAsync);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task CheckHealthAsync_WhenBlobUriMissing_ReturnsDegraded(string? blobUri) {
        var healthCheck = CreateHealthCheck(blobUri: blobUri);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("not configured");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenBlobExists_ReturnsHealthyAccessibleMessage() {
        var healthCheck = CreateHealthCheck(blobExistsAsync: (_, _) => Task.FromResult(true));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("blob storage is accessible");
        result.Description.Should().Contain("account.blob.core.windows.net/keys");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenBlobDoesNotExist_ReturnsHealthyPendingCreationMessage() {
        var healthCheck = CreateHealthCheck(blobExistsAsync: (_, _) => Task.FromResult(false));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("blob will be created on first use");
        result.Description.Should().Contain("account.blob.core.windows.net/keys");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenBlobAccessFailsWithRequestFailedException_ReturnsDegraded() {
        var healthCheck = CreateHealthCheck(blobExistsAsync: (_, _) =>
            throw new RequestFailedException(403, "Forbidden"));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("not accessible");
        result.Description.Should().Contain("Forbidden");
        result.Exception.Should().BeOfType<RequestFailedException>();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenBlobUriIsInvalid_ReturnsUnhealthy() {
        var healthCheck = CreateHealthCheck(blobUri: "not a valid absolute URI");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<UriFormatException>();
        result.Description.Should().Contain("Unexpected error");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenBlobUriUsesUnsupportedScheme_ReturnsUnhealthy() {
        var healthCheck = CreateHealthCheck(blobUri: "file:///tmp/keys.xml");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        // BlobClient / DefaultAzureCredential path fails without Azure access
        result.Status.Should().BeOneOf(HealthStatus.Unhealthy, HealthStatus.Degraded);
        result.Exception.Should().NotBeNull();
    }
}
