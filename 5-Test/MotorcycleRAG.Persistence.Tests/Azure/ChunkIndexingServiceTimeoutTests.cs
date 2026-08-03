using System.Text;
using Azure;
using Azure.Search.Documents;
using SearchOptions = MotorcycleRAG.Core.Options.SearchOptions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.ValueObjects;
using MotorcycleRAG.Persistence.Azure.Search;

namespace MotorcycleRAG.UnitTests.Azure;

/// <summary>
/// Verifies T7 (.NET half) acceptance criteria:
///  - #1: a stalled indexing call is cancelled at <see cref="SearchOptions.BatchIndexTimeoutSeconds"/>
///    and not allowed to hang the synchronous controller <c>await</c> indefinitely.
///  - #2: non-transient 4xx (404/400/401/403) failures are not retried by the Polly resilience
///    pipeline; transient 5xx/429/network failures are retried.
/// </summary>
/// <remarks>
/// Drives the batch upload through a mocked <see cref="SearchClient"/>. The resilience pipeline
/// is the production <see cref="SearchIndexResiliencePipelineProvider"/> (default options) so the
/// timeout-linked cancellation flows through the real Polly pipeline.
/// </remarks>
public class ChunkIndexingServiceTimeoutTests
{
    private static (Mock<ISearchClientFactory> factory, Mock<SearchClient> client) BuildFactory(Mock<SearchClient>? client = null)
    {
        client ??= new Mock<SearchClient>();
        var factory = new Mock<ISearchClientFactory>();
        factory.SetupGet(f => f.DefaultCategory).Returns(MotorcycleCategory.Sport);
        factory.Setup(f => f.GetIndexName(MotorcycleCategory.Sport)).Returns("motorcycle-sport");
        factory.Setup(f => f.GetClient(MotorcycleCategory.Sport)).Returns(client.Object);
        factory.Setup(f => f.IndexExistsAsync(MotorcycleCategory.Sport, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return (factory, client);
    }

    [Fact]
    public async Task StalledUpload_IsCancelledAtBatchIndexTimeoutSeconds_AndSurfacesAsTimeoutException()
    {
        // Arrange: a SearchClient whose MergeOrUploadDocumentsAsync throws OperationCanceledException
        // when its CancellationToken fires — the shape a real Azure SDK call takes on cancellation.
        var client = new Mock<SearchClient>();
        client
            .Setup(c => c.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<ChunkIndexingService.ChunkIndexRecord>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(new CancellationToken(canceled: true)));

        var (factory, _) = BuildFactory(client);

        // Two-second batch timeout keeps the test fast while still exercising the real path.
        var options = Options.Create(new SearchOptions { BatchIndexTimeoutSeconds = 2 });

        var sut = new ChunkIndexingService(
            factory.Object,
            Mock.Of<IMotorcycleCategoryClassifier>(),
            new SearchIndexResiliencePipelineProvider(),
            options,
            NullLogger<ChunkIndexingService>.Instance);

        // Build a one-chunk JSONL stream so exactly one batch upload is issued.
        var jsonl = "{\"id\":\"c1\",\"category\":\"sport\",\"make\":\"Honda\",\"model\":\"CBR600RR\"}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));

        // Act + Assert: the call must complete (not hang) and surface a TimeoutException whose
        // message references the configured BatchIndexTimeoutSeconds.
        var act = async () => await sut.IndexFromJsonlAsync(stream, Guid.NewGuid().ToString(), Guid.Empty, Guid.Empty, null, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<TimeoutException>();
        thrown.Which.Message.Should().Contain("timed out");
        thrown.Which.Message.Should().Contain("2");
    }

    [Fact]
    public async Task NonTransient404_DoesNotRetryAndSurfacesOriginalException()
    {
        // Arrange: a SearchClient whose MergeOrUploadDocumentsAsync throws a 404
        // RequestFailedException — a non-transient failure that must propagate immediately
        // (T7 transient-only: never retry 404/400/401/403). Counting invocations proves the
        // resilience pipeline did not retry.
        var client = new Mock<SearchClient>();
        client
            .Setup(c => c.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<ChunkIndexingService.ChunkIndexRecord>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found"));

        var (factory, _) = BuildFactory(client);

        var options = Options.Create(new SearchOptions { BatchIndexTimeoutSeconds = 30 });

        var sut = new ChunkIndexingService(
            factory.Object,
            Mock.Of<IMotorcycleCategoryClassifier>(),
            new SearchIndexResiliencePipelineProvider(),
            options,
            NullLogger<ChunkIndexingService>.Instance);

        var jsonl = "{\"id\":\"c1\",\"category\":\"sport\",\"make\":\"Honda\",\"model\":\"CBR600RR\"}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));

        // Act + Assert: exactly one call to MergeOrUploadDocumentsAsync (no retries), and the
        // surfaced exception is the original 404 RequestFailedException.
        var act = async () => await sut.IndexFromJsonlAsync(stream, Guid.NewGuid().ToString(), Guid.Empty, Guid.Empty, null, CancellationToken.None);

        (await act.Should().ThrowAsync<RequestFailedException>()).Which.Status.Should().Be(404);

        client.Verify(
            c => c.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<ChunkIndexingService.ChunkIndexRecord>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a 404 is non-transient and must not be retried (T7 transient-only)");
    }

    [Fact]
    public async Task Transient500_RetriesAndEventuallySurfacesOriginalException()
    {
        // Arrange: a SearchClient whose MergeOrUploadDocumentsAsync always throws a 500
        // RequestFailedException — a transient failure that the Polly pipeline must retry
        // (MaxRetryAttempts times) before surfacing. Counting invocations proves the retries
        // actually happened.
        var client = new Mock<SearchClient>();
        client
            .Setup(c => c.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<ChunkIndexingService.ChunkIndexRecord>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, "Internal Server Error"));

        var (factory, _) = BuildFactory(client);

        // Short timeout so the test does not wait for the cancellation grace period after the
        // retries exhaust (the resilience pipeline's retry count is independent of the timeout).
        var options = Options.Create(new SearchOptions { BatchIndexTimeoutSeconds = 30 });

        var sut = new ChunkIndexingService(
            factory.Object,
            Mock.Of<IMotorcycleCategoryClassifier>(),
            new SearchIndexResiliencePipelineProvider(),
            options,
            NullLogger<ChunkIndexingService>.Instance);

        var jsonl = "{\"id\":\"c1\",\"category\":\"sport\",\"make\":\"Honda\",\"model\":\"CBR600RR\"}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));

        // Act + Assert: the call must surface the 500 (after retries), and the client must have
        // been invoked more than once (proving the transient retry happened).
        var act = async () => await sut.IndexFromJsonlAsync(stream, Guid.NewGuid().ToString(), Guid.Empty, Guid.Empty, null, CancellationToken.None);

        await act.Should().ThrowAsync<RequestFailedException>();

        client.Verify(
            c => c.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<ChunkIndexingService.ChunkIndexRecord>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeast(2),
            "a 500 is transient and must be retried by the Polly resilience pipeline (T7)");
    }
}
