using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using MotorcycleRag.WebUI.BFF.HealthChecks;

namespace MotorcycleRag.WebUI.BFF.Tests.HealthChecks;

public class DataProtectionHealthCheckTests {
    private const string BlobUri = "https://account.blob.core.windows.net/keys/keys.xml";

    private static DataProtectionHealthCheck CreateHealthCheck(
        string? blobUri = BlobUri,
        IDataProtectionBlobProbe? blobProbe = null) {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["DataProtection:BlobUri"] = blobUri
            })
            .Build();

        return new DataProtectionHealthCheck(
            configuration,
            blobProbe ?? Mock.Of<IDataProtectionBlobProbe>(),
            Mock.Of<ILogger<DataProtectionHealthCheck>>());
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
        var blobProbe = new Mock<IDataProtectionBlobProbe>();
        blobProbe.Setup(probe => probe.ExistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var healthCheck = CreateHealthCheck(blobProbe: blobProbe.Object);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("blob storage is accessible");
        result.Description.Should().Contain("account.blob.core.windows.net/keys");
        blobProbe.Verify(probe => probe.ExistsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenBlobDoesNotExist_ReturnsHealthyPendingCreationMessage() {
        var blobProbe = new Mock<IDataProtectionBlobProbe>();
        blobProbe.Setup(probe => probe.ExistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var healthCheck = CreateHealthCheck(blobProbe: blobProbe.Object);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("blob will be created on first use");
        result.Description.Should().Contain("account.blob.core.windows.net/keys");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenBlobAccessFailsWithRequestFailedException_ReturnsDegraded() {
        var blobProbe = new Mock<IDataProtectionBlobProbe>();
        blobProbe.Setup(probe => probe.ExistsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "Forbidden"));
        var healthCheck = CreateHealthCheck(blobProbe: blobProbe.Object);

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
    public async Task CheckHealthAsync_WhenBlobUriUsesUnsupportedScheme_ReturnsUnhealthyWithoutProbing() {
        var blobProbe = new Mock<IDataProtectionBlobProbe>();
        var healthCheck = CreateHealthCheck(blobUri: "file:///tmp/keys.xml", blobProbe: blobProbe.Object);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<InvalidOperationException>();
        blobProbe.Verify(probe => probe.ExistsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void AzureBlobDataProtectionProbe_WhenBlobClientIsNull_ThrowsArgumentNullException() {
        var createProbe = () => new AzureBlobDataProtectionProbe(null!);

        createProbe.Should().Throw<ArgumentNullException>()
            .WithParameterName("blobClient");
    }

    [Fact]
    public async Task AzureBlobDataProtectionProbe_WhenBlobExists_ReturnsSdkResponseValue() {
        var blobClient = new Mock<BlobClient>(
            new Uri("https://account.blob.core.windows.net/keys/keys.xml"),
            new BlobClientOptions());
        blobClient
            .Setup(client => client.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));
        var probe = new AzureBlobDataProtectionProbe(blobClient.Object);

        var exists = await probe.ExistsAsync(CancellationToken.None);

        exists.Should().BeTrue();
        blobClient.Verify(client => client.ExistsAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenBlobUriHasNoContainer_ReturnsHealthyHostLocation() {
        var blobProbe = new Mock<IDataProtectionBlobProbe>();
        blobProbe.Setup(probe => probe.ExistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var healthCheck = CreateHealthCheck(
            blobUri: "https://account.blob.core.windows.net/",
            blobProbe: blobProbe.Object);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("account.blob.core.windows.net/");
    }
}
