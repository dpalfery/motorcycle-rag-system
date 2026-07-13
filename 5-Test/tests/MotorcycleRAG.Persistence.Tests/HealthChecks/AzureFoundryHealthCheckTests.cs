using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Moq.Contrib.HttpClient;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.HealthChecks;

namespace MotorcycleRAG.Persistence.Tests.HealthChecks;

public class AzureFoundryHealthCheckTests
{
    private static HealthCheckContext DefaultContext => new()
    {
        Registration = new HealthCheckRegistration("test", _ => null!, null, null)
    };

    private static AzureFoundryOptions ValidOptions => new()
    {
        FoundryEndpoint = "https://foundry.example.com"
    };

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenHttpClientFactoryIsNull()
    {
        // Act
        var act = () => new AzureFoundryHealthCheck(
            null!,
            TestHelpers.OptionsFor(ValidOptions),
            TestHelpers.CreateNullLogger<AzureFoundryHealthCheck>());

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsIsNull()
    {
        // Act
        var act = () => new AzureFoundryHealthCheck(
            Mock.Of<IHttpClientFactory>(),
            null!,
            TestHelpers.CreateNullLogger<AzureFoundryHealthCheck>());

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Act
        var act = () => new AzureFoundryHealthCheck(
            Mock.Of<IHttpClientFactory>(),
            TestHelpers.OptionsFor(ValidOptions),
            null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenOptionsValidationFails_ReturnsDegraded()
    {
        // Arrange
        var mockOptions = new Mock<IOptions<AzureFoundryOptions>>();
        mockOptions.Setup(o => o.Value).Throws(new OptionsValidationException(
            "AzureFoundry", typeof(AzureFoundryOptions), new[] { "Validation error" }));

        var sut = new AzureFoundryHealthCheck(
            Mock.Of<IHttpClientFactory>(),
            mockOptions.Object,
            TestHelpers.CreateNullLogger<AzureFoundryHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("not configured");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenEndpointIsEmpty_ReturnsUnhealthy()
    {
        // Arrange
        var options = new AzureFoundryOptions { FoundryEndpoint = "" };
        var sut = new AzureFoundryHealthCheck(
            Mock.Of<IHttpClientFactory>(),
            TestHelpers.OptionsFor(options),
            TestHelpers.CreateNullLogger<AzureFoundryHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Azure Foundry endpoint is not configured");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenEndpointIsInvalidUrl_ReturnsUnhealthy()
    {
        // Arrange
        var options = new AzureFoundryOptions { FoundryEndpoint = "not-a-valid-url!!!" };
        var sut = new AzureFoundryHealthCheck(
            Mock.Of<IHttpClientFactory>(),
            TestHelpers.OptionsFor(options),
            TestHelpers.CreateNullLogger<AzureFoundryHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("not a valid URL");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenEndpointReturnsSuccess_ReturnsHealthy()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Head, "https://foundry.example.com/")
            .ReturnsResponse(HttpStatusCode.OK);

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(handler.CreateClient());

        var sut = new AzureFoundryHealthCheck(
            mockFactory.Object,
            TestHelpers.OptionsFor(ValidOptions),
            TestHelpers.CreateNullLogger<AzureFoundryHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Azure Foundry is healthy");
        result.Data.Should().ContainKey("response_time_ms");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenEndpointReturnsNonSuccess_ReturnsDegraded()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Head, "https://foundry.example.com/")
            .ReturnsResponse(HttpStatusCode.ServiceUnavailable);

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(handler.CreateClient());

        var sut = new AzureFoundryHealthCheck(
            mockFactory.Object,
            TestHelpers.OptionsFor(ValidOptions),
            TestHelpers.CreateNullLogger<AzureFoundryHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("ServiceUnavailable");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenRequestTimesOut_ReturnsUnhealthy()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Head, "https://foundry.example.com/")
            .ThrowsAsync(new OperationCanceledException());

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(handler.CreateClient());

        var sut = new AzureFoundryHealthCheck(
            mockFactory.Object,
            TestHelpers.OptionsFor(ValidOptions),
            TestHelpers.CreateNullLogger<AzureFoundryHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("timed out");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenUnexpectedErrorOccurs_ReturnsUnhealthy()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Head, "https://foundry.example.com/")
            .ThrowsAsync(new InvalidOperationException("Something broke"));

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(handler.CreateClient());

        var sut = new AzureFoundryHealthCheck(
            mockFactory.Object,
            TestHelpers.OptionsFor(ValidOptions),
            TestHelpers.CreateNullLogger<AzureFoundryHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Something broke");
    }
}
