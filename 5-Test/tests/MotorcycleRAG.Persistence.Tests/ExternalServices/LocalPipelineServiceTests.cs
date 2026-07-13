using System.Net;
using Microsoft.Extensions.Options;
using Moq.Contrib.HttpClient;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.ExternalServices;

namespace MotorcycleRAG.Persistence.Tests.ExternalServices;

public class LocalPipelineServiceTests
{
    private const string LocalEndpoint = "http://localhost:8100";
    private const string UploadId = "upload-abc";
    private const string SourceToken = "sas-token-123";

    private static IOptions<IngestionOptions> CreateValidOptions()
        => Options.Create(new IngestionOptions
        {
            LocalEndpoint = LocalEndpoint,
            Mode = ProcessingMode.Local
        });

    private static IOptions<BlobStorageOptions> CreateBlobStorageOptions()
        => Options.Create(new BlobStorageOptions
        {
            RawUploadsContainer = "raw-uploads"
        });

    private static LocalPipelineService CreateSut(Mock<HttpMessageHandler>? handlerMock = null)
    {
        handlerMock ??= new Mock<HttpMessageHandler>();
        var factory = handlerMock.CreateClientFactory();
        return new LocalPipelineService(
            factory,
            CreateValidOptions(),
            CreateBlobStorageOptions(),
            TestHelpers.CreateNullLogger<LocalPipelineService>());
    }

    // ---- Constructor tests ----

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenHttpClientFactoryIsNull()
    {
        var act = () => new LocalPipelineService(
            null!, CreateValidOptions(), CreateBlobStorageOptions(),
            TestHelpers.CreateNullLogger<LocalPipelineService>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConfigIsNull()
    {
        var handler = new Mock<HttpMessageHandler>();
        var act = () => new LocalPipelineService(
            handler.CreateClientFactory(), null!, CreateBlobStorageOptions(),
            TestHelpers.CreateNullLogger<LocalPipelineService>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenBlobStorageOptionsIsNull()
    {
        var handler = new Mock<HttpMessageHandler>();
        var act = () => new LocalPipelineService(
            handler.CreateClientFactory(), CreateValidOptions(), null!,
            TestHelpers.CreateNullLogger<LocalPipelineService>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("blobStorageOptions");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var handler = new Mock<HttpMessageHandler>();
        var act = () => new LocalPipelineService(
            handler.CreateClientFactory(), CreateValidOptions(), CreateBlobStorageOptions(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ShouldThrowInvalidOperationException_WhenLocalEndpointIsMissing()
    {
        var handler = new Mock<HttpMessageHandler>();
        var emptyOptions = Options.Create(new IngestionOptions { LocalEndpoint = "" });
        var act = () => new LocalPipelineService(
            handler.CreateClientFactory(), emptyOptions, CreateBlobStorageOptions(),
            TestHelpers.CreateNullLogger<LocalPipelineService>());
        act.Should().Throw<InvalidOperationException>().WithMessage("*LocalEndpoint*required*");
    }

    [Fact]
    public void Constructor_ShouldSucceed_WithValidDependencies()
    {
        var handler = new Mock<HttpMessageHandler>();
        var act = () => new LocalPipelineService(
            handler.CreateClientFactory(), CreateValidOptions(), CreateBlobStorageOptions(),
            TestHelpers.CreateNullLogger<LocalPipelineService>());
        act.Should().NotThrow();
    }

    // ---- TriggerPipelineAsync tests ----

    [Fact]
    public async Task TriggerPipelineAsync_ShouldThrow_WhenUploadIdIsNullOrWhiteSpace()
    {
        var sut = CreateSut();
        var act = () => sut.TriggerPipelineAsync("", "manual-pdf", "pipeline-id", SourceToken);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("uploadId");
    }

    [Fact]
    public async Task TriggerPipelineAsync_ShouldThrow_WhenDocumentTypeIsNullOrWhiteSpace()
    {
        var sut = CreateSut();
        var act = () => sut.TriggerPipelineAsync(UploadId, "", "pipeline-id", SourceToken);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("documentType");
    }

    [Fact]
    public async Task TriggerPipelineAsync_ShouldThrow_WhenSourceTokenMissingForPdf()
    {
        var sut = CreateSut();
        var act = () => sut.TriggerPipelineAsync(UploadId, "manual-pdf", "pipeline-id", null);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("Source access token is required"));
    }

    [Fact]
    public async Task TriggerPipelineAsync_ShouldThrow_WhenSourceTokenMissingForCsv()
    {
        var sut = CreateSut();
        var act = () => sut.TriggerPipelineAsync(UploadId, "spec-dataset", "pipeline-id", null);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(ex => ex.Message.Contains("Source access token is required"));
    }

    [Fact]
    public async Task TriggerPipelineAsync_ShouldThrow_WhenDocumentTypeIsUnsupported()
    {
        var sut = CreateSut();
        var act = () => sut.TriggerPipelineAsync(UploadId, "unknown-type", "pipeline-id", SourceToken);
        await act.Should().ThrowAsync<ArgumentException>()
            .Where(ex => ex.Message.Contains("Unsupported document type"));
    }

    [Fact]
    public async Task TriggerPipelineAsync_ShouldReturnJobId_ForManualPdf()
    {
        var handler = new Mock<HttpMessageHandler>();
        var expectedJobId = "job-123";
        handler.SetupRequest(HttpMethod.Post, $"{LocalEndpoint}/process/pdf")
            .ReturnsJsonResponse(new { job_id = expectedJobId });

        var sut = CreateSut(handler);
        var result = await sut.TriggerPipelineAsync(UploadId, "manual-pdf", "pipeline-id", SourceToken);
        result.Should().Be(expectedJobId);
    }

    [Fact]
    public async Task TriggerPipelineAsync_ShouldReturnJobId_ForSpecDataset()
    {
        var handler = new Mock<HttpMessageHandler>();
        var expectedJobId = "job-456";
        handler.SetupRequest(HttpMethod.Post, $"{LocalEndpoint}/process/csv")
            .ReturnsJsonResponse(new { job_id = expectedJobId });

        var sut = CreateSut(handler);
        var result = await sut.TriggerPipelineAsync(UploadId, "spec-dataset", "pipeline-id", SourceToken);
        result.Should().Be(expectedJobId);
    }

    [Fact]
    public async Task TriggerPipelineAsync_ShouldReturnJobId_ForBikeGraph()
    {
        var handler = new Mock<HttpMessageHandler>();
        var expectedJobId = "job-graph";
        handler.SetupRequest(HttpMethod.Post, $"{LocalEndpoint}/process/bike-graph")
            .ReturnsJsonResponse(new { job_id = expectedJobId });

        var sut = CreateSut(handler);
        var result = await sut.TriggerPipelineAsync(UploadId, "bike-graph", "pipeline-id", SourceToken);
        result.Should().Be(expectedJobId);
    }

    [Fact]
    public async Task TriggerPipelineAsync_ShouldWork_WhenPipelineIdIsNull()
    {
        var handler = new Mock<HttpMessageHandler>();
        var expectedJobId = "job-without-pipeline";
        handler.SetupRequest(HttpMethod.Post, $"{LocalEndpoint}/process/pdf")
            .ReturnsJsonResponse(new { job_id = expectedJobId });

        var sut = CreateSut(handler);
        var result = await sut.TriggerPipelineAsync(UploadId, "manual-pdf", null!, SourceToken);
        result.Should().Be(expectedJobId);
    }

    // ---- GetRunStatusAsync tests ----

    [Fact]
    public async Task GetRunStatusAsync_ShouldThrow_WhenRunIdIsNullOrWhiteSpace()
    {
        var sut = CreateSut();
        var act = () => sut.GetRunStatusAsync("", "pipeline-id");
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("runId");
    }

    [Fact]
    public async Task GetRunStatusAsync_ShouldReturnStatus_OnSuccess()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, $"{LocalEndpoint}/jobs/job-123")
            .ReturnsJsonResponse(new { status = "Succeeded", message = "Processing complete" });

        var sut = CreateSut(handler);
        var result = await sut.GetRunStatusAsync("job-123", "pipeline-id");
        result.Should().NotBeNull();
        result.Status.Should().Be("Succeeded");
        result.Message.Should().Be("Processing complete");
    }

    [Fact]
    public async Task GetRunStatusAsync_ShouldReturnErrorInfo_WhenPresent()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, $"{LocalEndpoint}/jobs/job-err")
            .ReturnsJsonResponse(new { status = "Failed", error = "Something went wrong" });

        var sut = CreateSut(handler);
        var result = await sut.GetRunStatusAsync("job-err", "pipeline-id");
        result.Status.Should().Be("Failed");
        result.Error.Should().Be("Something went wrong");
    }

    [Fact]
    public async Task GetRunStatusAsync_ShouldReturnUnknown_WhenStatusFieldMissing()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, $"{LocalEndpoint}/jobs/job-no-status")
            .ReturnsJsonResponse(new { message = "no status" });

        var sut = CreateSut(handler);
        var result = await sut.GetRunStatusAsync("job-no-status", "pipeline-id");
        result.Status.Should().Be("Unknown");
    }

    [Fact]
    public async Task GetRunStatusAsync_ShouldWork_WhenPipelineIdIsNull()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.SetupRequest(HttpMethod.Get, $"{LocalEndpoint}/jobs/job-123")
            .ReturnsJsonResponse(new { status = "Running" });

        var sut = CreateSut(handler);
        var result = await sut.GetRunStatusAsync("job-123", null!);
        result.Status.Should().Be("Running");
    }

    [Fact]
    public void Implements_ILocalPipelineService()
    {
        var sut = CreateSut();
        sut.Should().BeAssignableTo<ILocalPipelineService>();
    }
}
