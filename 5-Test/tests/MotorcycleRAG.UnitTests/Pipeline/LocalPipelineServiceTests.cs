using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Contrib.HttpClient;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.ExternalServices;

namespace MotorcycleRAG.UnitTests.Pipeline;

/// <summary>
/// Unit tests for <see cref="LocalPipelineService"/>.
/// Verifies HTTP routing by document type, error handling, and constructor null checks.
/// </summary>
public sealed class LocalPipelineServiceTests : IDisposable {
    private const string LocalEndpoint = "http://localhost:8100";

    private readonly Mock<HttpMessageHandler> _handler;
    private readonly Mock<IHttpClientFactory> _factory;
    private readonly Mock<ILogger<LocalPipelineService>> _logger;
    private readonly IOptions<IngestionOptions> _options;
    private readonly IOptions<BlobStorageOptions> _blobOptions;
    private readonly HttpClient _httpClient;

    public LocalPipelineServiceTests() {
        _handler = new Mock<HttpMessageHandler>();
        _logger = new Mock<ILogger<LocalPipelineService>>();
        _options = Options.Create(new IngestionOptions {
            LocalEndpoint = LocalEndpoint
        });
        _blobOptions = Options.Create(new BlobStorageOptions {
            RawUploadsContainer = "raw-uploads"
        });

        _httpClient = _handler.CreateClient();
        _httpClient.BaseAddress = new Uri(LocalEndpoint);

        _factory = new Mock<IHttpClientFactory>();
        _factory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(_httpClient);
    }

    public void Dispose() {
        _httpClient.Dispose();
    }

    #region Constructor null checks

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenHttpClientFactoryIsNull() {
        // Act & Assert
        var act = () => new LocalPipelineService(null!, _options, _blobOptions, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsIsNull() {
        // Act & Assert
        var act = () => new LocalPipelineService(_factory.Object, null!, _blobOptions, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull() {
        // Act & Assert
        var act = () => new LocalPipelineService(_factory.Object, _options, _blobOptions, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    #endregion

    #region TriggerPipelineAsync

    [Fact]
    public async Task TriggerPipelineAsync_WithManualPdfDocumentType_ShouldPostToPdfEndpoint() {
        // Arrange
        _handler
            .SetupRequest(HttpMethod.Post, $"{LocalEndpoint}/process/pdf")
            .ReturnsResponse(HttpStatusCode.OK, """{"job_id": "job-abc123"}""", "application/json");

        var sut = CreateSut();

        // Act
        var runId = await sut.TriggerPipelineAsync("upload-1", "manual-pdf", "ignored-pipeline-id", "test-token");

        // Assert
        runId.Should().Be("job-abc123");
        _handler.VerifyRequest(HttpMethod.Post, $"{LocalEndpoint}/process/pdf", Times.Once());
    }

    [Fact]
    public async Task TriggerPipelineAsync_WithSpecDatasetDocumentType_ShouldPostToCsvEndpoint() {
        // Arrange
        _handler
            .SetupRequest(HttpMethod.Post, $"{LocalEndpoint}/process/csv")
            .ReturnsResponse(HttpStatusCode.OK, """{"job_id": "job-csv-456"}""", "application/json");

        var sut = CreateSut();

        // Act
        var runId = await sut.TriggerPipelineAsync("upload-2", "spec-dataset", "ignored-pipeline-id", "test-token");

        // Assert
        runId.Should().Be("job-csv-456");
        _handler.VerifyRequest(HttpMethod.Post, $"{LocalEndpoint}/process/csv", Times.Once());
    }

    [Fact]
    public async Task TriggerPipelineAsync_WithBikeGraphDocumentType_ShouldPostToBikeGraphEndpoint() {
        // Arrange
        _handler
            .SetupRequest(HttpMethod.Post, $"{LocalEndpoint}/process/bike-graph")
            .ReturnsResponse(HttpStatusCode.OK, """{"job_id": "job-graph-789"}""", "application/json");

        var sut = CreateSut();

        // Act
        var runId = await sut.TriggerPipelineAsync("upload-graph", "bike-graph", "ignored-pipeline-id");

        // Assert
        runId.Should().Be("job-graph-789");
        _handler.VerifyRequest(HttpMethod.Post, $"{LocalEndpoint}/process/bike-graph", Times.Once());
    }

    [Fact]
    public async Task TriggerPipelineAsync_WithNonSuccessStatusCode_ShouldThrowInvalidOperationException() {
        // Arrange
        _handler
            .SetupAnyRequest()
            .ReturnsResponse(HttpStatusCode.InternalServerError);

        var sut = CreateSut();

        // Act
        var act = () => sut.TriggerPipelineAsync("upload-3", "manual-pdf", "ignored-pipeline-id", "test-token");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*500*");
    }

    [Fact]
    public async Task TriggerPipelineAsync_WithoutSourceAccessToken_ShouldThrowInvalidOperationException() {
        var sut = CreateSut();

        var act = () => sut.TriggerPipelineAsync("upload-4", "manual-pdf", "ignored-pipeline-id");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Source access token is required*");
    }

    #endregion

    #region GetRunStatusAsync

    [Fact]
    public async Task GetRunStatusAsync_ShouldReturnStatus_WhenJobExists() {
        // Arrange
        const string runId = "job-abc123";

        _handler
            .SetupRequest(HttpMethod.Get, $"{LocalEndpoint}/jobs/{runId}")
            .ReturnsResponse(HttpStatusCode.OK, """{"status": "completed"}""", "application/json");

        var sut = CreateSut();

        // Act
        var result = await sut.GetRunStatusAsync(runId, "ignored-pipeline-id");

        // Assert
        result.Status.Should().Be("completed");
    }

    [Fact]
    public async Task GetRunStatusAsync_ShouldReturnErrorDetails_WhenJobFailed() {
        const string runId = "job-failed";

        _handler
            .SetupRequest(HttpMethod.Get, $"{LocalEndpoint}/jobs/{runId}")
            .ReturnsResponse(
                HttpStatusCode.OK,
                """{"status": "failed", "message": "PDF processing failed", "error": "BlobWriter has no storage client configured."}""",
                "application/json");

        var sut = CreateSut();

        var result = await sut.GetRunStatusAsync(runId, "ignored-pipeline-id");

        result.Status.Should().Be("failed");
        result.Message.Should().Be("PDF processing failed");
        result.Error.Should().Contain("BlobWriter");
    }

    [Fact]
    public async Task GetRunStatusAsync_WithNonSuccessStatusCode_ShouldThrowInvalidOperationException() {
        // Arrange
        const string runId = "job-missing";

        _handler
            .SetupRequest(HttpMethod.Get, $"{LocalEndpoint}/jobs/{runId}")
            .ReturnsResponse(HttpStatusCode.NotFound);

        var sut = CreateSut();

        // Act
        var act = () => sut.GetRunStatusAsync(runId, "ignored-pipeline-id");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*404*");
    }

    #endregion

    #region Helpers

    private LocalPipelineService CreateSut() =>
        new(_factory.Object, _options, _blobOptions, _logger.Object);

    #endregion
}
