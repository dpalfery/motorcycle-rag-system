using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Moq.Contrib.HttpClient;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.HealthChecks;

namespace MotorcycleRAG.Persistence.Tests.HealthChecks;

public class AzureSearchHealthCheckTests
{
    private static HealthCheckContext DefaultContext => new()
    {
        Registration = new HealthCheckRegistration("test", _ => null!, null, null)
    };

    private static AzureFoundryOptions ValidOptions => new()
    {
        SearchServiceEndpoint = "https://search.example.com"
    };

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsIsNull()
    {
        // Act
        var act = () => new AzureSearchHealthCheck(
            null!,
            Mock.Of<IHttpClientFactory>(),
            TestHelpers.CreateNullLogger<AzureSearchHealthCheck>());

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenHttpClientFactoryIsNull()
    {
        // Act
        var act = () => new AzureSearchHealthCheck(
            TestHelpers.OptionsFor(ValidOptions),
            null!,
            TestHelpers.CreateNullLogger<AzureSearchHealthCheck>());

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Act
        var act = () => new AzureSearchHealthCheck(
            TestHelpers.OptionsFor(ValidOptions),
            Mock.Of<IHttpClientFactory>(),
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

        var sut = new AzureSearchHealthCheck(
            mockOptions.Object,
            Mock.Of<IHttpClientFactory>(),
            TestHelpers.CreateNullLogger<AzureSearchHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("not configured");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenSearchEndpointIsEmpty_ReturnsUnhealthy()
    {
        // Arrange
        var options = new AzureFoundryOptions { SearchServiceEndpoint = "" };
        var sut = new AzureSearchHealthCheck(
            TestHelpers.OptionsFor(options),
            Mock.Of<IHttpClientFactory>(),
            TestHelpers.CreateNullLogger<AzureSearchHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("not configured");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenSearchEndpointIsInvalidUrl_ReturnsUnhealthy()
    {
        // Arrange
        var options = new AzureFoundryOptions { SearchServiceEndpoint = "invalid-url" };
        var sut = new AzureSearchHealthCheck(
            TestHelpers.OptionsFor(options),
            Mock.Of<IHttpClientFactory>(),
            TestHelpers.CreateNullLogger<AzureSearchHealthCheck>());

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
        handler.SetupRequest(HttpMethod.Head, "https://search.example.com/")
            .ReturnsResponse(HttpStatusCode.OK);

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(handler.CreateClient());

        var sut = new AzureSearchHealthCheck(
            TestHelpers.OptionsFor(ValidOptions),
            mockFactory.Object,
            TestHelpers.CreateNullLogger<AzureSearchHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Azure Search is healthy");
        result.Data.Should().ContainKey("response_time_ms");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenEndpointReturnsForbidden_ReturnsHealthy()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Head, "https://search.example.com/")
            .ReturnsResponse(HttpStatusCode.Forbidden);

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(handler.CreateClient());

        var sut = new AzureSearchHealthCheck(
            TestHelpers.OptionsFor(ValidOptions),
            mockFactory.Object,
            TestHelpers.CreateNullLogger<AzureSearchHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenEndpointReturnsNonSuccessNonForbidden_ReturnsDegraded()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Head, "https://search.example.com/")
            .ReturnsResponse(HttpStatusCode.NotFound);

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(handler.CreateClient());

        var sut = new AzureSearchHealthCheck(
            TestHelpers.OptionsFor(ValidOptions),
            mockFactory.Object,
            TestHelpers.CreateNullLogger<AzureSearchHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("NotFound");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenRequestTimesOut_ReturnsUnhealthy()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Head, "https://search.example.com/")
            .ThrowsAsync(new OperationCanceledException());

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(handler.CreateClient());

        var sut = new AzureSearchHealthCheck(
            TestHelpers.OptionsFor(ValidOptions),
            mockFactory.Object,
            TestHelpers.CreateNullLogger<AzureSearchHealthCheck>());

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
        handler.SetupRequest(HttpMethod.Head, "https://search.example.com/")
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(handler.CreateClient());

        var sut = new AzureSearchHealthCheck(
            TestHelpers.OptionsFor(ValidOptions),
            mockFactory.Object,
            TestHelpers.CreateNullLogger<AzureSearchHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Connection refused");
    }
}
