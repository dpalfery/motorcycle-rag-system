using Azure;
using Azure.Core;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Persistence.Azure.Search;
using System.ClientModel.Primitives;

using MotorcycleRAG.Contracts.Models.DTOs.Search;
namespace MotorcycleRAG.Persistence.Tests.Azure.Search;

public sealed class AzureSearchDocumentServiceTests
{
    private readonly Mock<ISearchClientFactory> _clientFactoryMock = new();
    private readonly Mock<IResilienceService> _resilienceServiceMock = new();
    private readonly Mock<ICorrelationService> _correlationServiceMock = new();

    private AzureSearchDocumentService CreateSut() =>
        new(
            _clientFactoryMock.Object,
            TestHelpers.CreateNullLogger<AzureSearchDocumentService>(),
            _resilienceServiceMock.Object,
            _correlationServiceMock.Object);

    // ---- Constructor ----

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenClientFactoryIsNull()
    {
        var act = () => new AzureSearchDocumentService(
            null!,
            TestHelpers.CreateNullLogger<AzureSearchDocumentService>(),
            _resilienceServiceMock.Object,
            _correlationServiceMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var act = () => new AzureSearchDocumentService(
            _clientFactoryMock.Object,
            null!,
            _resilienceServiceMock.Object,
            _correlationServiceMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenResilienceServiceIsNull()
    {
        var act = () => new AzureSearchDocumentService(
            _clientFactoryMock.Object,
            TestHelpers.CreateNullLogger<AzureSearchDocumentService>(),
            null!,
            _correlationServiceMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("resilienceService");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenCorrelationServiceIsNull()
    {
        var act = () => new AzureSearchDocumentService(
            _clientFactoryMock.Object,
            TestHelpers.CreateNullLogger<AzureSearchDocumentService>(),
            _resilienceServiceMock.Object,
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("correlationService");
    }

    // ---- IndexDocumentsAsync (generic) ----

    [Fact]
    public async Task IndexDocumentsAsync_WithNullArray_ShouldReturnTrue()
    {
        var sut = CreateSut();

        var result = await sut.IndexDocumentsAsync<MotorcycleDocumentDto>(null!);

        result.Should().BeTrue();
        _resilienceServiceMock.Verify(
            x => x.ExecuteAsync<bool>(It.IsAny<string>(), It.IsAny<Func<Task<bool>>>(), It.IsAny<Func<Task<bool>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IndexDocumentsAsync_WithEmptyArray_ShouldReturnTrue()
    {
        var sut = CreateSut();

        var result = await sut.IndexDocumentsAsync(Array.Empty<MotorcycleDocumentDto>());

        result.Should().BeTrue();
        _resilienceServiceMock.Verify(
            x => x.ExecuteAsync<bool>(It.IsAny<string>(), It.IsAny<Func<Task<bool>>>(), It.IsAny<Func<Task<bool>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IndexDocumentsAsync_WithDocuments_ShouldDelegateToResilienceService()
    {
        var sut = CreateSut();
        var correlationId = "corr-001";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.IndexDocuments",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var documents = new MotorcycleDocumentDto[] { new() { Id = "doc1" } };

        var result = await sut.IndexDocumentsAsync(documents);

        result.Should().BeTrue();
        _resilienceServiceMock.Verify(
            x => x.ExecuteAsync<bool>(
                "AzureSearch.IndexDocuments",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IndexDocumentsAsync_WhenResilienceReturnsFalse_ShouldReturnFalse()
    {
        var sut = CreateSut();
        var correlationId = "corr-fail";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.IndexDocuments",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var documents = new MotorcycleDocumentDto[] { new() { Id = "doc1" } };

        var result = await sut.IndexDocumentsAsync(documents);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IndexDocumentsAsync_WithCancellationToken_ShouldPassToken()
    {
        var sut = CreateSut();
        var correlationId = "corr-ct";
        var cts = new CancellationTokenSource();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.IndexDocuments",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                cts.Token))
            .ReturnsAsync(true);
        var documents = new MotorcycleDocumentDto[] { new() { Id = "doc1" } };

        var result = await sut.IndexDocumentsAsync(documents, cts.Token);

        result.Should().BeTrue();
    }

    // ---- IndexDocumentsAsync (IEnumerable<MotorcycleDocumentDto>) ----

    [Fact]
    public async Task IndexDocumentsAsync_EnumerableWithEmptyDocuments_ShouldReturnWithoutCallingResilience()
    {
        var sut = CreateSut();

        await sut.IndexDocumentsAsync(Enumerable.Empty<MotorcycleDocumentDto>());

        _resilienceServiceMock.Verify(
            x => x.ExecuteAsync<bool>(It.IsAny<string>(), It.IsAny<Func<Task<bool>>>(), It.IsAny<Func<Task<bool>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IndexDocumentsAsync_EnumerableWhenResilienceReturnsFalse_ShouldThrowInvalidOperationException()
    {
        var sut = CreateSut();
        var correlationId = "corr-001";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.IndexDocuments",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var documents = new List<MotorcycleDocumentDto> { new() { Id = "doc1" } };

        var act = async () => await sut.IndexDocumentsAsync(documents);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Error indexing*");
    }

    [Fact]
    public async Task IndexDocumentsAsync_EnumerableWhenResilienceReturnsTrue_ShouldCompleteSuccessfully()
    {
        var sut = CreateSut();
        var correlationId = "corr-success";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.IndexDocuments",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var documents = new List<MotorcycleDocumentDto>
        {
            new() { Id = "doc1", Title = "Test Doc 1" },
            new() { Id = "doc2", Title = "Test Doc 2" }
        };

        await sut.IndexDocumentsAsync(documents);

        // Should not throw
        _resilienceServiceMock.Verify(
            x => x.ExecuteAsync<bool>(
                "AzureSearch.IndexDocuments",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IndexDocumentsAsync_EnumerableWhenResilienceThrows_ShouldWrapException()
    {
        var sut = CreateSut();
        var correlationId = "corr-wrap";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.IndexDocuments",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Timed out"));
        var documents = new List<MotorcycleDocumentDto> { new() { Id = "doc1" } };

        var act = async () => await sut.IndexDocumentsAsync(documents);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Error indexing*")
            .WithInnerException<InvalidOperationException, TimeoutException>();
    }

    [Fact]
    public async Task IndexDocumentsAsync_EnumerableWithSingleDocument_ShouldIndexSuccessfully()
    {
        var sut = CreateSut();
        var correlationId = "corr-single";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.IndexDocuments",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var documents = new List<MotorcycleDocumentDto> { new() { Id = "only-doc" } };

        await sut.IndexDocumentsAsync(documents);

        // Should not throw
    }

    // ---- DeleteDocumentsAsync ----

    [Fact]
    public async Task DeleteDocumentsAsync_WithEmptyIds_ShouldReturnWithoutCallingResilience()
    {
        var sut = CreateSut();

        await sut.DeleteDocumentsAsync(Array.Empty<string>());

        _resilienceServiceMock.Verify(
            x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Func<Task>>(), It.IsAny<Func<Task>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteDocumentsAsync_WithIds_ShouldDelegateToResilienceService()
    {
        var sut = CreateSut();
        var correlationId = "corr-002";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync(
                "AzureSearch.DeleteDocuments",
                It.IsAny<Func<Task>>(),
                It.IsAny<Func<Task>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var ids = new[] { "id1", "id2" };

        await sut.DeleteDocumentsAsync(ids);

        _resilienceServiceMock.Verify(
            x => x.ExecuteAsync(
                "AzureSearch.DeleteDocuments",
                It.IsAny<Func<Task>>(),
                It.IsAny<Func<Task>>(),
                correlationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteDocumentsAsync_WhenResilienceThrows_ShouldWrapException()
    {
        var sut = CreateSut();
        var correlationId = "corr-del-err";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync(
                "AzureSearch.DeleteDocuments",
                It.IsAny<Func<Task>>(),
                It.IsAny<Func<Task>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Delete timed out"));
        var ids = new[] { "id1" };

        var act = async () => await sut.DeleteDocumentsAsync(ids);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Error deleting documents")
            .WithInnerException<InvalidOperationException, TimeoutException>();
    }

    [Fact]
    public async Task DeleteDocumentsAsync_WithSingleId_ShouldSucceed()
    {
        var sut = CreateSut();
        var correlationId = "corr-single-del";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync(
                "AzureSearch.DeleteDocuments",
                It.IsAny<Func<Task>>(),
                It.IsAny<Func<Task>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var ids = new[] { "only-id" };

        await sut.DeleteDocumentsAsync(ids);

        // Should not throw
    }

    // ---- CreateOrUpdateIndexAsync ----

    [Fact]
    public async Task CreateOrUpdateIndexAsync_ShouldThrowNotSupportedException()
    {
        var sut = CreateSut();
        var correlationId = "corr-003";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.CreateIndex",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns((string key, Func<Task<bool>> op, Func<Task<bool>> fb, string cid, CancellationToken ct) => op());

        var act = async () => await sut.CreateOrUpdateIndexAsync("test-index");

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Runtime index creation is not supported*");
    }

    [Fact]
    public async Task CreateOrUpdateIndexAsync_WithCancellationToken_ShouldPassToken()
    {
        var sut = CreateSut();
        var correlationId = "corr-ct-create";
        var cts = new CancellationTokenSource();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.CreateIndex",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                cts.Token))
            .Returns((string key, Func<Task<bool>> op, Func<Task<bool>> fb, string cid, CancellationToken ct) => op());

        var act = async () => await sut.CreateOrUpdateIndexAsync("test-index", cts.Token);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Runtime index creation is not supported*");
    }

    [Fact]
    public async Task CreateOrUpdateIndexAsync_WithDifferentIndexNames_ShouldIncludeNameInMessage()
    {
        var sut = CreateSut();
        var correlationId = "corr-name";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.CreateIndex",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns((string key, Func<Task<bool>> op, Func<Task<bool>> fb, string cid, CancellationToken ct) => op());

        var act = async () => await sut.CreateOrUpdateIndexAsync("motorcycle-dirt");

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*motorcycle-dirt*");
    }

    // ---- ExecuteIndexDocumentsAsync inner flow (success path) ----

    [Fact]
    public async Task IndexDocumentsAsync_Generic_WhenUploadAllSucceed_ReturnsTrue()
    {
        var sut = CreateSut();
        var correlationId = "corr-exec-ok";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);

        // Resilience invokes the operation delegate
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.IndexDocuments",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns(async (string key, Func<Task<bool>> op, Func<Task<bool>> fb, string cid, CancellationToken ct) =>
                await op());

        // Build a real IndexDocumentsResult via ModelReaderWriter JSON deserialization
        var result = CreateIndexDocumentsResult(("doc1", true, 200, null));
        var mockRawResponse = new Mock<global::Azure.Response>();
        var azureResponse = global::Azure.Response.FromValue(result, mockRawResponse.Object);

        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.UploadDocumentsAsync<MotorcycleDocumentDto>(
                It.IsAny<IEnumerable<MotorcycleDocumentDto>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(azureResponse);

        _clientFactoryMock.Setup(x => x.GetDefaultClient()).Returns(searchClient.Object);

        var documents = new MotorcycleDocumentDto[] { new() { Id = "doc1" } };
        var success = await sut.IndexDocumentsAsync(documents);

        success.Should().BeTrue();
    }

    [Fact]
    public async Task IndexDocumentsAsync_Generic_WhenUploadHasFailures_ReturnsFalse()
    {
        var sut = CreateSut();
        var correlationId = "corr-exec-fail";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.IndexDocuments",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns(async (string key, Func<Task<bool>> op, Func<Task<bool>> fb, string cid, CancellationToken ct) =>
                await op());

        var result = CreateIndexDocumentsResult(("doc1", false, 400, "Invalid document"));
        var mockRawResponse = new Mock<global::Azure.Response>();
        var azureResponse = global::Azure.Response.FromValue(result, mockRawResponse.Object);

        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.UploadDocumentsAsync<MotorcycleDocumentDto>(
                It.IsAny<IEnumerable<MotorcycleDocumentDto>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(azureResponse);

        _clientFactoryMock.Setup(x => x.GetDefaultClient()).Returns(searchClient.Object);

        var documents = new MotorcycleDocumentDto[] { new() { Id = "doc1" } };
        var success = await sut.IndexDocumentsAsync(documents);

        success.Should().BeFalse();
    }

    [Fact]
    public async Task IndexDocumentsAsync_Generic_WhenUploadHasMixedResults_ReturnsFalse()
    {
        var sut = CreateSut();
        var correlationId = "corr-exec-mix";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.IndexDocuments",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns(async (string key, Func<Task<bool>> op, Func<Task<bool>> fb, string cid, CancellationToken ct) =>
                await op());

        var result = CreateIndexDocumentsResult(
            ("doc1", true, 200, null),
            ("doc2", false, 422, "Unprocessable"));
        var mockRawResponse = new Mock<global::Azure.Response>();
        var azureResponse = global::Azure.Response.FromValue(result, mockRawResponse.Object);

        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.UploadDocumentsAsync<MotorcycleDocumentDto>(
                It.IsAny<IEnumerable<MotorcycleDocumentDto>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(azureResponse);

        _clientFactoryMock.Setup(x => x.GetDefaultClient()).Returns(searchClient.Object);

        var documents = new MotorcycleDocumentDto[] { new() { Id = "doc1" }, new() { Id = "doc2" } };
        var success = await sut.IndexDocumentsAsync(documents);

        success.Should().BeFalse();
    }

    // ---- ExecuteDeleteDocumentsAsync inner flow ----

    [Fact]
    public async Task DeleteDocumentsAsync_WhenAllDeletesSucceed_CompletesSuccessfully()
    {
        var sut = CreateSut();
        var correlationId = "corr-del-ok";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync(
                "AzureSearch.DeleteDocuments",
                It.IsAny<Func<Task>>(),
                It.IsAny<Func<Task>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns(async (string key, Func<Task> op, Func<Task> fb, string cid, CancellationToken ct) =>
                await op());

        var result = CreateIndexDocumentsResult(("id1", true, 200, null));
        var mockRawResponse = new Mock<global::Azure.Response>();
        var azureResponse = global::Azure.Response.FromValue(result, mockRawResponse.Object);

        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.DeleteDocumentsAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(azureResponse);

        _clientFactoryMock.Setup(x => x.GetDefaultClient()).Returns(searchClient.Object);

        var ids = new[] { "id1" };
        await sut.DeleteDocumentsAsync(ids);
    }

    [Fact]
    public async Task DeleteDocumentsAsync_WhenSomeDeletesFail_CompletesWithWarning()
    {
        var sut = CreateSut();
        var correlationId = "corr-del-partial";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync(
                "AzureSearch.DeleteDocuments",
                It.IsAny<Func<Task>>(),
                It.IsAny<Func<Task>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns(async (string key, Func<Task> op, Func<Task> fb, string cid, CancellationToken ct) =>
                await op());

        var result = CreateIndexDocumentsResult(("id1", false, 404, "Not found"));
        var mockRawResponse = new Mock<global::Azure.Response>();
        var azureResponse = global::Azure.Response.FromValue(result, mockRawResponse.Object);

        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.DeleteDocumentsAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(azureResponse);

        _clientFactoryMock.Setup(x => x.GetDefaultClient()).Returns(searchClient.Object);

        var ids = new[] { "id1" };
        await sut.DeleteDocumentsAsync(ids);
    }

    // ---- DeleteDocumentsAsync - fallback invocation ----

    [Fact]
    public async Task DeleteDocumentsAsync_WhenOperationFailsAndFallbackRuns_ShouldCompleteWithoutThrowing()
    {
        var sut = CreateSut();
        var correlationId = "corr-del-fallback";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);

        // Operation throws, waterfall to fallback which succeeds
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync(
                "AzureSearch.DeleteDocuments",
                It.IsAny<Func<Task>>(),
                It.IsAny<Func<Task>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns(async (string key, Func<Task> op, Func<Task> fb, string cid, CancellationToken ct) =>
            {
                try { await op(); }
                catch { /* resilience catches and calls fallback */ }
                await fb();
            });

        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.DeleteDocumentsAsync(
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Search service unavailable"));

        _clientFactoryMock.Setup(x => x.GetDefaultClient()).Returns(searchClient.Object);

        var ids = new[] { "id1" };
        var act = async () => await sut.DeleteDocumentsAsync(ids);

        // Should not throw since the fallback runs and completes
        await act.Should().NotThrowAsync();
    }

    // ---- IndexDocumentsAsync (generic) - resilience invokes fallback ----

    [Fact]
    public async Task IndexDocumentsAsync_Generic_WhenResilienceInvokesFallback_ReturnsFalse()
    {
        var sut = CreateSut();
        var correlationId = "corr-fallback-idx";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);

        // The resilience fallback returns false for IndexDocuments
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.IndexDocuments",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .Returns(async (string key, Func<Task<bool>> op, Func<Task<bool>> fb, string cid, CancellationToken ct) =>
                await fb());

        var documents = new MotorcycleDocumentDto[] { new() { Id = "doc1" } };
        var success = await sut.IndexDocumentsAsync(documents);

        success.Should().BeFalse();
    }

    private static IndexDocumentsResult CreateIndexDocumentsResult(
        params (string Key, bool Succeeded, int Status, string? ErrorMessage)[] items)
    {
        var valueItems = items.Select(i =>
        {
            if (i.Succeeded)
                return $$"""{"key":"{{i.Key}}","status":true,"statusCode":{{i.Status}}}""";
            var errMsg = i.ErrorMessage ?? "Error";
            return $$"""{"key":"{{i.Key}}","status":false,"statusCode":{{i.Status}},"errorMessage":"{{errMsg}}"}""";
        });
        var json = $$"""{"value":[{{string.Join(",", valueItems)}}]}""";
        return ModelReaderWriter.Read<IndexDocumentsResult>(BinaryData.FromString(json))!;
    }
}
