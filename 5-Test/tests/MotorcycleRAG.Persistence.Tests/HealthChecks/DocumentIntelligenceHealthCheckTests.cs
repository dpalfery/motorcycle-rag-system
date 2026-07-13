using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Moq.Contrib.HttpClient;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.HealthChecks;

namespace MotorcycleRAG.Persistence.Tests.HealthChecks;

public class DocumentIntelligenceHealthCheckTests
{
    private static HealthCheckContext DefaultContext => new()
    {
        Registration = new HealthCheckRegistration("test", _ => null!, null, null)
    };

    private static AzureFoundryOptions ValidOptions => new()
    {
        DocumentIntelligenceEndpoint = "https://di.example.com"
    };

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsIsNull()
    {
        // Act
        var act = () => new DocumentIntelligenceHealthCheck(
            null!,
            Mock.Of<IHttpClientFactory>(),
            TestHelpers.CreateNullLogger<DocumentIntelligenceHealthCheck>());

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenHttpClientFactoryIsNull()
    {
        // Act
        var act = () => new DocumentIntelligenceHealthCheck(
            TestHelpers.OptionsFor(ValidOptions),
            null!,
            TestHelpers.CreateNullLogger<DocumentIntelligenceHealthCheck>());

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("httpClientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Act
        var act = () => new DocumentIntelligenceHealthCheck(
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

        var sut = new DocumentIntelligenceHealthCheck(
            mockOptions.Object,
            Mock.Of<IHttpClientFactory>(),
            TestHelpers.CreateNullLogger<DocumentIntelligenceHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("not configured");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenEndpointIsEmpty_ReturnsDegraded()
    {
        // Arrange
        var options = new AzureFoundryOptions { DocumentIntelligenceEndpoint = "" };
        var sut = new DocumentIntelligenceHealthCheck(
            TestHelpers.OptionsFor(options),
            Mock.Of<IHttpClientFactory>(),
            TestHelpers.CreateNullLogger<DocumentIntelligenceHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("not configured");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenEndpointIsInvalidUrl_ReturnsDegraded()
    {
        // Arrange
        var options = new AzureFoundryOptions { DocumentIntelligenceEndpoint = "not-a-url" };
        var sut = new DocumentIntelligenceHealthCheck(
            TestHelpers.OptionsFor(options),
            Mock.Of<IHttpClientFactory>(),
            TestHelpers.CreateNullLogger<DocumentIntelligenceHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("not a valid URL");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenEndpointReturnsSuccess_ReturnsHealthy()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Head, "https://di.example.com/")
            .ReturnsResponse(HttpStatusCode.OK);

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(handler.CreateClient());

        var sut = new DocumentIntelligenceHealthCheck(
            TestHelpers.OptionsFor(ValidOptions),
            mockFactory.Object,
            TestHelpers.CreateNullLogger<DocumentIntelligenceHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Document Intelligence is healthy");
        result.Data.Should().ContainKey("response_time_ms");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenEndpointReturnsForbidden_ReturnsHealthy()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Head, "https://di.example.com/")
            .ReturnsResponse(HttpStatusCode.Forbidden);

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(handler.CreateClient());

        var sut = new DocumentIntelligenceHealthCheck(
            TestHelpers.OptionsFor(ValidOptions),
            mockFactory.Object,
            TestHelpers.CreateNullLogger<DocumentIntelligenceHealthCheck>());

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
        handler.SetupRequest(HttpMethod.Head, "https://di.example.com/")
            .ReturnsResponse(HttpStatusCode.InternalServerError);

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(handler.CreateClient());

        var sut = new DocumentIntelligenceHealthCheck(
            TestHelpers.OptionsFor(ValidOptions),
            mockFactory.Object,
            TestHelpers.CreateNullLogger<DocumentIntelligenceHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("InternalServerError");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenRequestTimesOut_ReturnsUnhealthy()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Head, "https://di.example.com/")
            .ThrowsAsync(new OperationCanceledException());

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(handler.CreateClient());

        var sut = new DocumentIntelligenceHealthCheck(
            TestHelpers.OptionsFor(ValidOptions),
            mockFactory.Object,
            TestHelpers.CreateNullLogger<DocumentIntelligenceHealthCheck>());

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
        handler.SetupRequest(HttpMethod.Head, "https://di.example.com/")
            .ThrowsAsync(new HttpRequestException("DNS failure"));

        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(handler.CreateClient());

        var sut = new DocumentIntelligenceHealthCheck(
            TestHelpers.OptionsFor(ValidOptions),
            mockFactory.Object,
            TestHelpers.CreateNullLogger<DocumentIntelligenceHealthCheck>());

        // Act
        var result = await sut.CheckHealthAsync(DefaultContext);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("DNS failure");
    }
}
