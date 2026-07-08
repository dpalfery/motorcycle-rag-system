using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Persistence.Azure.Search;

namespace MotorcycleRAG.UnitTests.Azure;

/// <summary>
/// T12 acceptance criterion: <see cref="AzureSearchDocumentService.ExecuteCreateIndexAsync"/> must
/// NOT silently return <c>true</c> from a <c>Task.Delay(300)</c> stub. Per D1/D4 the API must not
/// create indexes at runtime; Pulumi owns index provisioning. Any runtime caller must fail loudly
/// with <see cref="NotSupportedException"/> rather than lie about success.
/// </summary>
public class AzureSearchDocumentServiceCreateIndexTests
{
    private readonly Mock<ISearchClientFactory> _clientFactory = new();
    private readonly Mock<IResilienceService> _resilienceService = new();
    private readonly Mock<ICorrelationService> _correlationService = new();

    public AzureSearchDocumentServiceCreateIndexTests()
    {
        _correlationService.Setup(c => c.GetOrCreateCorrelationId()).Returns("test-correlation");

        // Passthrough: invoke the real callback with no swallowing/fallback, so the
        // NotSupportedException thrown by ExecuteCreateIndexAsync propagates unchanged.
        // This exercises the real production code path (no in-memory Search shim).
        _resilienceService
            .Setup(r => r.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<Task<bool>>, Func<Task<bool>>?, string?, CancellationToken>(
                (_, op, _, _, _) => op());
    }

    [Fact]
    public async Task CreateOrUpdateIndexAsync_ShouldThrow_NotSupportedException_InsteadOfReturningTrue()
    {
        // Arrange
        var sut = new AzureSearchDocumentService(
            _clientFactory.Object,
            NullLogger<AzureSearchDocumentService>.Instance,
            _resilienceService.Object,
            _correlationService.Object);

        // Act
        var act = () => sut.CreateOrUpdateIndexAsync("motorcycle-dev-index", CancellationToken.None);

        // Assert: the stub must never silently return true. Per D1/D4 it must throw
        // NotSupportedException so any caller fails loudly and the request is rejected.
        var ex = await act.Should().ThrowAsync<NotSupportedException>();
        ex.Which.Message.Should().Contain("motorcycle-dev-index");
        ex.Which.Message.Should().Contain("Pulumi");
    }
}
