using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq.Protected;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Azure.Search;

namespace MotorcycleRAG.Persistence.Tests.Azure.Search;

public sealed class InMemorySearchShimChunkIndexingServiceTests
{
    private static SearchOptions CreateOptions(string? shimEndpoint = "http://localhost:9090")
    {
        return new SearchOptions
        {
            InMemoryShimEndpoint = shimEndpoint,
            BatchIndexTimeoutSeconds = 30,
            MaxSearchResults = 10
        };
    }

    private static string CreateJsonl(params (string id, int pageNumber, int chunkIndex)[] chunks)
    {
        var lines = new List<string>();
        foreach (var (id, page, chunk) in chunks)
        {
            var record = new Dictionary<string, object>
            {
                ["id"] = id,
                ["title"] = $"Title for {id}",
                ["content"] = $"Content for {id}",
                ["documentType"] = "manual",
                ["category"] = "sport",
                ["pageNumber"] = page,
                ["chunkIndex"] = chunk,
                ["contentVector"] = new float[] { 0.1f, 0.2f, 0.3f },
                ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O")
            };
            lines.Add(JsonSerializer.Serialize(record));
        }
        return string.Join("\n", lines);
    }

    private Stream CreateJsonlStream(params (string id, int pageNumber, int chunkIndex)[] chunks)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(CreateJsonl(chunks)));
    }

    // ---- Constructor ----

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenHttpClientIsNull()
    {
        var options = Options.Create(CreateOptions());

        var act = () => new InMemorySearchShimChunkIndexingService(
            null!,
            options,
            TestHelpers.CreateNullLogger<InMemorySearchShimChunkIndexingService>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsIsNull()
    {
        using var httpClient = new HttpClient();

        var act = () => new InMemorySearchShimChunkIndexingService(
            httpClient,
            null!,
            TestHelpers.CreateNullLogger<InMemorySearchShimChunkIndexingService>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsValueIsNull()
    {
        using var httpClient = new HttpClient();
        var optionsMock = new Mock<IOptions<SearchOptions>>();
        optionsMock.Setup(x => x.Value).Returns((SearchOptions)null!);

        var act = () => new InMemorySearchShimChunkIndexingService(
            httpClient,
            optionsMock.Object,
            TestHelpers.CreateNullLogger<InMemorySearchShimChunkIndexingService>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        using var httpClient = new HttpClient();

        var act = () => new InMemorySearchShimChunkIndexingService(
            httpClient,
            Microsoft.Extensions.Options.Options.Create(CreateOptions()),
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ---- IndexFromJsonlAsync - validation ----

    [Fact]
    public async Task IndexFromJsonlAsync_ShouldThrowArgumentNullException_WhenStreamIsNull()
    {
        using var httpClient = new HttpClient();
        var sut = new InMemorySearchShimChunkIndexingService(
            httpClient,
            Microsoft.Extensions.Options.Options.Create(CreateOptions()),
            TestHelpers.CreateNullLogger<InMemorySearchShimChunkIndexingService>());

        var act = async () => await sut.IndexFromJsonlAsync(null!, "upload-1");

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("jsonlStream");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task IndexFromJsonlAsync_ShouldThrowArgumentException_WhenUploadIdIsBlank(string? uploadId)
    {
        using var httpClient = new HttpClient();
        var sut = new InMemorySearchShimChunkIndexingService(
            httpClient,
            Options.Create(CreateOptions()),
            TestHelpers.CreateNullLogger<InMemorySearchShimChunkIndexingService>());

        using var stream = CreateJsonlStream(("chunk-1", 1, 0));

        var act = async () => await sut.IndexFromJsonlAsync(stream, uploadId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("uploadId");
    }

    [Fact]
    public async Task IndexFromJsonlAsync_ShouldThrowInvalidOperationException_WhenEndpointIsNotValid()
    {
        using var httpClient = new HttpClient();
        var sut = new InMemorySearchShimChunkIndexingService(
            httpClient,
            Options.Create(CreateOptions("not-a-valid-uri")),
            TestHelpers.CreateNullLogger<InMemorySearchShimChunkIndexingService>());
        using var stream = CreateJsonlStream(("chunk-1", 1, 0));

        var act = async () => await sut.IndexFromJsonlAsync(stream, "upload-1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*InMemoryShimEndpoint must be configured*");
    }

    [Fact]
    public async Task IndexFromJsonlAsync_ShouldThrowInvalidOperationException_WhenEndpointIsNotLoopback()
    {
        using var httpClient = new HttpClient();
        var sut = new InMemorySearchShimChunkIndexingService(
            httpClient,
            Options.Create(CreateOptions("https://remote.example.com")),
            TestHelpers.CreateNullLogger<InMemorySearchShimChunkIndexingService>());
        using var stream = CreateJsonlStream(("chunk-1", 1, 0));

        var act = async () => await sut.IndexFromJsonlAsync(stream, "upload-1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must be an http or https loopback URL*");
    }

    // ---- IndexFromJsonlAsync - success ----

    [Fact]
    public async Task IndexFromJsonlAsync_ShouldPostJsonl_WhenEndpointIsValid()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Loose);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.PathAndQuery.Contains("index-jsonl") &&
                    req.Content!.Headers.ContentType!.MediaType == "application/x-ndjson"),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{}")
            });

        using var httpClient = new HttpClient(handlerMock.Object);
        var sut = new InMemorySearchShimChunkIndexingService(
            httpClient,
            Options.Create(CreateOptions("http://localhost:9090")),
            TestHelpers.CreateNullLogger<InMemorySearchShimChunkIndexingService>());
        using var stream = CreateJsonlStream(("chunk-1", 1, 0), ("chunk-2", 2, 1));

        // Act
        var result = await sut.IndexFromJsonlAsync(stream, "upload-1");

        // Assert
        result.TotalParsed.Should().Be(2);
        result.BatchCount.Should().Be(1);
        result.Outcomes.Should().HaveCount(2);
        result.Outcomes.Should().AllSatisfy(o =>
        {
            o.Succeeded.Should().BeTrue();
            o.FailureReason.Should().BeNull();
        });
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Post),
            ItExpr.IsAny<CancellationToken>());
    }

    // ---- IndexFromJsonlAsync - HTTP error ----

    [Fact]
    public async Task IndexFromJsonlAsync_ShouldReturnFailedOutcomes_WhenServerReturnsError()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Loose);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("Internal Server Error")
            });

        using var httpClient = new HttpClient(handlerMock.Object);
        var sut = new InMemorySearchShimChunkIndexingService(
            httpClient,
            Options.Create(CreateOptions("http://localhost:9090")),
            TestHelpers.CreateNullLogger<InMemorySearchShimChunkIndexingService>());
        using var stream = CreateJsonlStream(("chunk-1", 1, 0));

        // Act
        var result = await sut.IndexFromJsonlAsync(stream, "upload-1");

        // Assert
        result.TotalParsed.Should().Be(1);
        result.Outcomes.Should().HaveCount(1);
        result.Outcomes[0].Succeeded.Should().BeFalse();
        result.Outcomes[0].FailureReason.Should().Contain("500");
    }

    // ---- IndexFromJsonlAsync - loopback variants ----

    [Theory]
    [InlineData("http://localhost:9090")]
    [InlineData("https://localhost:9090")]
    [InlineData("http://127.0.0.1:9090")]
    [InlineData("https://127.0.0.1:9090")]
    public async Task IndexFromJsonlAsync_ShouldAcceptLoopbackEndpoints(string endpoint)
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Loose);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{}")
            });

        using var httpClient = new HttpClient(handlerMock.Object);
        var sut = new InMemorySearchShimChunkIndexingService(
            httpClient,
            Options.Create(CreateOptions(endpoint)),
            TestHelpers.CreateNullLogger<InMemorySearchShimChunkIndexingService>());
        using var stream = CreateJsonlStream(("chunk-1", 1, 0));

        // Act
        var result = await sut.IndexFromJsonlAsync(stream, "upload-loopback");

        // Assert
        result.TotalParsed.Should().Be(1);
        result.Outcomes.Should().AllSatisfy(o => o.Succeeded.Should().BeTrue());
    }

    // ---- IndexFromJsonlAsync - missing id in JSONL ----

    [Fact]
    public async Task IndexFromJsonlAsync_ShouldThrowJsonException_WhenChunkRecordMissingId()
    {
        // Arrange
        var jsonl = "{\"title\":\"test\",\"content\":\"test content\"}\n";
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{}")
            });

        using var httpClient = new HttpClient(handlerMock.Object);
        var sut = new InMemorySearchShimChunkIndexingService(
            httpClient,
            Options.Create(CreateOptions("http://127.0.0.1:9090")),
            TestHelpers.CreateNullLogger<InMemorySearchShimChunkIndexingService>());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));

        // Act
        var act = async () => await sut.IndexFromJsonlAsync(stream, "upload-missing-id");

        // Assert
        await act.Should().ThrowAsync<JsonException>()
            .WithMessage("*missing required field 'id'*");
    }
}
