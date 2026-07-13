using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;
using MotorcycleRAG.Domain.Constants;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using Xunit;

namespace MotorcycleRAG.IntegrationTests.Api;

public class ManualIngestionApiIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private static readonly JsonSerializerOptions JsonOptions = GetJsonOptions();

    private static JsonSerializerOptions GetJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public ManualIngestionApiIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAllDocuments_AsAdmin_ReturnsOk()
    {
        // Arrange
        var mockRepo = new Mock<IManualDocumentRepository>();
        mockRepo.Setup(r => r.GetAllDocumentsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManualDocument>());

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockRepo.Object);
            });
        }).CreateClient();

        client.DefaultRequestHeaders.Add("X-Test-Auth", "mcr-api-admin");

        // Act
        var response = await client.GetAsync("/api/manual-ingestion/manual-documents");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RegisterDocument_AsAdmin_ReturnsOk()
    {
        // Arrange
        var mockRepo = new Mock<IManualDocumentRepository>();
        var mockBlob = new Mock<IBlobStorageService>();

        mockBlob.Setup(b => b.UploadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://test.blob/manual.pdf");

        mockRepo.Setup(r => r.CreateDocumentAsync(It.IsAny<ManualDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManualDocument d, CancellationToken ct) => d);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockRepo.Object);
                services.AddSingleton(mockBlob.Object);
            });
        }).CreateClient();

        client.DefaultRequestHeaders.Add("X-Test-Auth", "mcr-api-admin");

        using var content = new MultipartFormDataContent();
        using var fileNameContent = new StringContent("manual.pdf");
        content.Add(fileNameContent, "SourceFileName");
        using var docTypeContent = new StringContent("manual-pdf");
        content.Add(docTypeContent, "DocumentType");
        using var makeContent = new StringContent("Honda");
        content.Add(makeContent, "Make");
        using var modelContent = new StringContent("CB500");
        content.Add(modelContent, "Model");
        using var yearContent = new StringContent("2020");
        content.Add(yearContent, "Year");
        
        using var fileContent = new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // PDF header
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "manual.pdf");

        // Act
        var response = await client.PostAsync("/api/manual-ingestion/manual-documents", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ManualDocumentDto>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("manual.pdf", result.SourceFileName);
    }

    [Fact]
    public async Task UploadIngestionJobPdf_AsAdmin_ReturnsAccepted()
    {
        // Arrange
        var mockBlob = new Mock<IBlobStorageService>();
        mockBlob.Setup(b => b.UploadAsync(
                "raw-uploads",
                It.Is<string>(name => name.EndsWith("/source.pdf", StringComparison.Ordinal)),
                It.IsAny<Stream>(),
                "application/pdf",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://test.blob/raw-uploads/source.pdf");

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockBlob.Object);
            });
        }).CreateClient();

        client.DefaultRequestHeaders.Add("X-Test-Auth", "mcr-api-admin");

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(fileContent, "file", "manual.pdf");

        // Act
        var response = await client.PostAsync("/api/ingestion/jobs/upload?documentType=manual-pdf", content);

        // Assert
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Accepted,
            $"Expected {HttpStatusCode.Accepted}, got {response.StatusCode}. Body: {responseBody}");

        var result = JsonSerializer.Deserialize<IngestionUploadResponse>(responseBody, JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("manual.pdf", result.FileName);
        Assert.Equal("manual-pdf", result.DocumentType);
        Assert.Equal("uploaded", result.Status);
    }

    [Fact]
    public void IngestionUploadMultipartLimit_UsesConfiguredIngestionLimit()
    {
        using var scope = _factory.Services.CreateScope();

        var ingestionOptions = scope.ServiceProvider.GetRequiredService<IOptions<IngestionOptions>>().Value;
        var formOptions = scope.ServiceProvider.GetRequiredService<IOptions<FormOptions>>().Value;

        Assert.Equal(ingestionOptions.MaxInputBytes, formOptions.MultipartBodyLengthLimit);
    }

    [Fact]
    public async Task ReportStageStart_AsLocalProcessor_ReturnsOk()
    {
        // Arrange
        var mockRepo = new Mock<IManualDocumentRepository>();
        mockRepo.Setup(r => r.CreateStageAsync(It.IsAny<ManualProcessingStage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManualProcessingStage s, CancellationToken ct) => s);
        
        mockRepo.Setup(r => r.GetRunByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ManualProcessingRun { RunId = Guid.NewGuid(), DocumentId = Guid.NewGuid() });

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockRepo.Object);
            });
        }).CreateClient();

        // Use a role that satisfies LocalProcessor policy: "File.Upload.All"
        client.DefaultRequestHeaders.Add("X-Test-Auth", "File.Upload.All");

        var runId = Guid.NewGuid();
        var request = new ManualStageStartRequest(null);

        // Act
        var response = await client.PostAsJsonAsync($"/api/manual-ingestion/manual-runs/{runId}/stages/{ManualIngestionStages.Source}/start", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetOperations_IncludesGraphSeedingJobs()
    {
        // Arrange
        var mockManualRepo = new Mock<IManualDocumentRepository>();
        mockManualRepo.Setup(r => r.GetRecentManualOperationsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(ManualDocument, ManualProcessingRun)>());

        var mockJobRepo = new Mock<IIngestionJobRepository>();
        mockJobRepo.Setup(r => r.GetRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IngestionJob>
            {
                new IngestionJob
                {
                    IngestionJobId = Guid.NewGuid(),
                    InputType = IngestionJobType.BikeGraph,
                    Status = IngestionJobStatus.Completed,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    SourceFileName = "graph.json"
                }
            });

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockManualRepo.Object);
                services.AddSingleton(mockJobRepo.Object);
            });
        }).CreateClient();

        client.DefaultRequestHeaders.Add("X-Test-Auth", "mcr-api-admin");

        // Act
        var response = await client.GetAsync("/api/manual-ingestion/operations");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IEnumerable<UnifiedOperationDto>>(JsonOptions);
        Assert.NotNull(result);
        Assert.Contains(result, op => op.OperationType == "GraphSeeding" && op.TargetIdentity == "graph.json");
    }
}
