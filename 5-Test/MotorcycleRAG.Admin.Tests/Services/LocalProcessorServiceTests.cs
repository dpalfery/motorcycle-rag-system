using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Services.Dtos;
using System.Net;
using System.Text;

namespace MotorcycleRAG.Admin.Tests.Services;

public sealed class LocalProcessorServiceTests {
    [Fact]
    public void BuildChildEnvironmentVariables_IncludesConfiguredApiBaseUrlAndPersistedUploadSecret() {
        var processorLogDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var configurationStateService = new TestConfigurationStateService {
            ApiBaseUrl = new Uri("https://localhost:7215"),
            EmbeddingProviderEndpoint = "http://localhost:5272",
            EmbeddingModel = "qwen3-embedding",
            LocalProcessorUploadJobSecret = "test-upload-secret"
        };

        var sut = new LocalProcessorService(configurationStateService, NullLogger<LocalProcessorService>.Instance, processorLogDirectory);

        var variables = sut.BuildChildEnvironmentVariables();

        variables.Should().ContainKey("PYTHONUNBUFFERED")
            .WhoseValue.Should().Be("1");
        variables.Should().ContainKey("LOCAL_PROCESSOR_LOG_DIR")
            .WhoseValue.Should().Be(processorLogDirectory);
        variables.Should().ContainKey("EMBEDDING_PROVIDER_ENDPOINT")
            .WhoseValue.Should().Be("http://localhost:5272");
        variables.Should().ContainKey("EMBEDDING_MODEL")
            .WhoseValue.Should().Be("qwen3-embedding");
        variables.Should().ContainKey("PYTHON_UPLOAD_JOB_SECRET")
            .WhoseValue.Should().Be("test-upload-secret");
        variables.Should().ContainKey("MCR_API_BASE_URL")
            .WhoseValue.Should().Be("https://localhost:7215");

        Directory.Exists(processorLogDirectory).Should().BeTrue();
        Directory.Delete(processorLogDirectory, recursive: true);
    }

    [Fact]
    public void BuildChildEnvironmentVariables_OmitsOptionalValuesWhenNotConfigured() {
        var processorLogDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var configurationStateService = new TestConfigurationStateService();

        var sut = new LocalProcessorService(configurationStateService, NullLogger<LocalProcessorService>.Instance, processorLogDirectory);

        var variables = sut.BuildChildEnvironmentVariables();

        variables.Should().ContainKey("PYTHONUNBUFFERED")
            .WhoseValue.Should().Be("1");
        variables.Should().ContainKey("LOCAL_PROCESSOR_LOG_DIR")
            .WhoseValue.Should().Be(processorLogDirectory);
        variables.Should().NotContainKey("EMBEDDING_PROVIDER_ENDPOINT");
        variables.Should().NotContainKey("EMBEDDING_MODEL");
        variables.Should().NotContainKey("PYTHON_UPLOAD_JOB_SECRET");
        variables.Should().NotContainKey("MCR_API_BASE_URL");

        Directory.Exists(processorLogDirectory).Should().BeTrue();
        Directory.Delete(processorLogDirectory, recursive: true);
    }

    [Fact]
    public void RecentProcessOutput_FallsBackToProcessorLogWhenLiveOutputIsEmpty() {
        var processorLogDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(processorLogDirectory);
        File.WriteAllLines(
            Path.Combine(processorLogDirectory, "local-processor.log"),
            [
                "2026-04-23 08:15:26,199 INFO     httpx - upload started",
                "2026-04-23 08:15:36,399 ERROR    processors.bike_graph_processor - Artifact upload failed"
            ]);

        var configurationStateService = new TestConfigurationStateService();
        var sut = new LocalProcessorService(configurationStateService, NullLogger<LocalProcessorService>.Instance, processorLogDirectory);

        sut.RecentProcessOutput.Should().ContainInOrder(
            "2026-04-23 08:15:26,199 INFO     httpx - upload started",
            "2026-04-23 08:15:36,399 ERROR    processors.bike_graph_processor - Artifact upload failed");

        Directory.Delete(processorLogDirectory, recursive: true);
    }

    [Fact]
    public async Task GetEmbeddingModelsAsync_DiscoversOpenAiCompatibleProviderWithoutLocalProcessor() {
        using var handler = new StubHttpMessageHandler(request => request.RequestUri?.AbsoluteUri switch {
            "http://127.0.0.1:1234/models" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "http://127.0.0.1:1234/v1/models" => new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(
                    "{\"data\":[{\"id\":\"text-embedding-qwen\"},{\"id\":\"chat-model\"}]}",
                    Encoding.UTF8,
                    "application/json")
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var sut = CreateSubject(handler);

        var result = await sut.GetEmbeddingModelsAsync("http://127.0.0.1:1234");

        result.Provider.Should().Be("openai-compatible");
        result.Endpoint.Should().Be("http://127.0.0.1:1234/v1");
        result.Models.Should().Equal("text-embedding-qwen", "chat-model");
    }

    [Fact]
    public async Task GetEmbeddingModelsAsync_AcceptsModelListUrlAndNormalizesIt() {
        using var handler = new StubHttpMessageHandler(request => request.RequestUri?.AbsoluteUri switch {
            "http://127.0.0.1:1234/v1/models" => new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(
                    "{\"data\":[{\"id\":\"text-embedding-qwen\"}]}",
                    Encoding.UTF8,
                    "application/json")
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var sut = CreateSubject(handler);

        var result = await sut.GetEmbeddingModelsAsync("http://127.0.0.1:1234/v1/models");

        result.Provider.Should().Be("openai-compatible");
        result.Endpoint.Should().Be("http://127.0.0.1:1234/v1");
        result.Models.Should().ContainSingle().Which.Should().Be("text-embedding-qwen");
    }

    [Fact]
    public async Task GetEmbeddingModelsAsync_AcceptsOllamaTagsUrlAndNormalizesIt() {
        using var handler = new StubHttpMessageHandler(request => request.RequestUri?.AbsoluteUri switch {
            "http://localhost:11434/models" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "http://localhost:11434/v1/models" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "http://localhost:11434/api/tags" => new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(
                    "{\"models\":[{\"model\":\"qwen3-embedding\"},{\"model\":\"qwen3:4b\"}]}",
                    Encoding.UTF8,
                    "application/json")
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var sut = CreateSubject(handler);

        var result = await sut.GetEmbeddingModelsAsync("http://localhost:11434/api/tags");

        result.Provider.Should().Be("ollama");
        result.Endpoint.Should().Be("http://localhost:11434");
        result.Models.Should().Equal("qwen3-embedding", "qwen3:4b");
    }

    [Fact]
    public async Task GetHealthAsync_ReturnsPayload_WhenHealthEndpointReturnsNonSuccessWithValidJson() {
        using var handler = new StubHttpMessageHandler(request => request.RequestUri?.AbsoluteUri switch {
            "http://processor.test:8100/health" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) {
                Content = new StringContent(
                    """
                    {
                      "status": "degraded",
                      "accepting_work": false,
                      "shutdown_requested": false,
                      "active_jobs": 2,
                      "message": "warming up"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var sut = CreateSubject(
            handler,
            localProcessorEndpoint: new Uri("http://processor.test:8100"));

        var result = await sut.GetHealthAsync();

        result.Should().BeEquivalentTo(new LocalProcessorHealthResponse {
            Status = "degraded",
            AcceptingWork = false,
            ShutdownRequested = false,
            ActiveJobs = 2,
            Message = "warming up"
        });
    }

    [Fact]
    public async Task GetHealthAsync_ReturnsNull_WhenHealthEndpointReturnsNonSuccessWithInvalidBody() {
        using var handler = new StubHttpMessageHandler(request => request.RequestUri?.AbsoluteUri switch {
            "http://processor.test:8100/health" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) {
                Content = new StringContent("not-json", Encoding.UTF8, "text/plain")
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var sut = CreateSubject(
            handler,
            localProcessorEndpoint: new Uri("http://processor.test:8100"));

        var result = await sut.GetHealthAsync();

        result.Should().BeNull();
    }

    private static LocalProcessorService CreateSubject(HttpMessageHandler? httpMessageHandler = null, Uri? localProcessorEndpoint = null) {
        var processorLogDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var configurationStateService = new TestConfigurationStateService {
            LocalProcessorEndpoint = localProcessorEndpoint
        };
        return new LocalProcessorService(configurationStateService, NullLogger<LocalProcessorService>.Instance, processorLogDirectory, httpMessageHandler);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            return Task.FromResult(handler(request));
        }
    }

    private sealed class TestConfigurationStateService : IConfigurationStateService {
        public bool IsConfigured => IsApiConfigured && IsAuthConfigured;

        public bool IsApiConfigured => ApiBaseUrl is not null;

        public bool IsAuthConfigured => !string.IsNullOrWhiteSpace(AuthClientId)
            && !string.IsNullOrWhiteSpace(AuthAuthority)
            && !string.IsNullOrWhiteSpace(AuthScope);

        public Uri? ApiBaseUrl { get; init; }

        public string? AuthClientId { get; init; }

        public string? AuthAuthority { get; init; }

        public string? AuthScope { get; init; }

        public string? EmbeddingProviderEndpoint { get; init; }

        public string? EmbeddingModel { get; init; }

        public Uri? LocalProcessorEndpoint { get; init; }

        public string? LocalProcessorWorkingDirectory { get; init; }

        public string? LocalProcessorStartCommand { get; init; }

        public int PdfChunkerMaxTokens { get; init; }

        public int CsvChunkMaxTokens { get; init; }

        public string? PdfChunkerTokenizer { get; init; }

        public string? LocalProcessorUploadJobSecret { get; init; }

        public bool IsLocalProcessorConfigured { get; init; }

        public event EventHandler? ConfigurationChanged;

        public Task LoadConfigurationAsync() => Task.CompletedTask;

        public Task SaveApiBaseUrlAsync(Uri? url) => Task.CompletedTask;

        public Task SaveAuthConfigurationAsync(string clientId, string authority, string scope) => Task.CompletedTask;

        public Task SaveEmbeddingConfigurationAsync(string? providerEndpoint, string? model) => Task.CompletedTask;

        public Task SaveLocalProcessorConfigurationAsync(
            Uri? endpoint,
            string? workingDirectory,
            string? startCommand,
            string? uploadJobSecret,
            int pdfChunkerMaxTokens,
            int csvChunkMaxTokens,
            string? pdfChunkerTokenizer) => Task.CompletedTask;

        public Task ClearConfigurationAsync() => Task.CompletedTask;

        public void RaiseConfigurationChanged() => ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }
}
