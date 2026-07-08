using System.Text;
using Azure.Search.Documents;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Domain.ValueObjects;
using Azure.Search.Documents.Models;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Persistence.Azure.Search;
using MotorcycleRAG.UnitTests.Logging;

namespace MotorcycleRAG.UnitTests.Azure;

/// <summary>
/// Verifies T10 acceptance criterion #1: each batch upserted to Azure AI Search emits one
/// structured summary log line carrying the succeeded/failed counts, the aggregated failure
/// reasons, and the target index name, so indexing failures are diagnosable from logs rather
/// than silent.
/// </summary>
/// <remarks>
/// Drives the failure path (the <c>SearchClient</c> throws) so the failure-reason aggregation
/// is exercised without relying on the in-memory Search shim or constructing Azure SDK response
/// models. The category is embedded on every chunk so the classifier is never invoked.
/// </remarks>
public class ChunkIndexingServicePerBatchLogsTests
{
    [Fact]
    public async Task FailedBatch_EmitsStructuredSummary_WithCountsReasonsAndIndexName()
    {
        var spy = new SpyLogger<ChunkIndexingService>();

        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<ChunkIndexingService.ChunkIndexRecord>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("index not found (404)"));

        var factory = new Mock<ISearchClientFactory>();
        factory.SetupGet(f => f.DefaultCategory).Returns(MotorcycleCategory.Dirt);
        factory.Setup(f => f.GetIndexName(MotorcycleCategory.Dirt)).Returns("motorcycle-dirt");
        factory.Setup(f => f.GetClient(MotorcycleCategory.Dirt)).Returns(searchClient.Object);
        factory.Setup(f => f.IndexExistsAsync(MotorcycleCategory.Dirt, It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        var sut = new ChunkIndexingService(
            factory.Object,
            Mock.Of<IMotorcycleCategoryClassifier>(),
            // Real (default) resilience pipeline so the injected failure propagates to the
            // batch catch and the structured summary is emitted — a Mock.Of<>() returns a
            // null Pipeline, which masks the failure under a NullReferenceException.
            new SearchIndexResiliencePipelineProvider(),
            Microsoft.Extensions.Options.Options.Create(new MotorcycleRAG.Core.Options.SearchOptions()),
            spy);

        var jsonl = "{\"id\":\"c1\",\"category\":\"dirt\"}\n{\"id\":\"c2\",\"category\":\"dirt\"}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));

        await sut.IndexFromJsonlAsync(stream, uploadId: Guid.NewGuid().ToString(), CancellationToken.None);

        // The batch-end summary is the only Information log that carries BOTH IndexName and
        // DurationMs (the start line has IndexName but no duration; the per-upload aggregate
        // has neither) — so it can be singled out unambiguously.
        var summary = spy.Entries.Should().ContainSingle(
                e => e.LogLevel == LogLevel.Information
                    && e.Properties.ContainsKey("IndexName")
                    && e.Properties.ContainsKey("DurationMs"),
                "each batch must emit exactly one structured summary line")
            .Which;

        summary.Properties["Succeeded"].Should().Be(0);
        summary.Properties["Total"].Should().Be(2);
        summary.Properties["IndexName"].Should().Be("motorcycle-dirt");
        summary.Properties["FailedCount"].Should().Be(2);
        summary.Properties["Category"].Should().Be("dirt");
        summary.Properties.Should().ContainKey("DurationMs");

        var reasons = summary.Properties["FailureReasons"].Should().BeOfType<string>().Which;
        reasons.Should().Contain("index not found (404)");
    }
}
