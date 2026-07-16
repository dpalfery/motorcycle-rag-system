using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs.Graph;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.UnitTests.Services.Ingestion;

public sealed class ManualBikeLinkerTests
{
    [Fact]
    public void Constructor_WhenDependencyIsNull_ThrowsArgumentNullException()
    {
        var bikeModelRepository = new Mock<IBikeModelRepository>().Object;
        var graphRepository = new Mock<IGraphRepository>().Object;
        var logger = NullLogger<ManualBikeLinker>.Instance;

        var missingBikeModelRepository = () => new ManualBikeLinker(null!, graphRepository, logger);
        var missingGraphRepository = () => new ManualBikeLinker(bikeModelRepository, null!, logger);
        var missingLogger = () => new ManualBikeLinker(bikeModelRepository, graphRepository, null!);

        missingBikeModelRepository.Should().Throw<ArgumentNullException>()
            .WithParameterName("bikeModelRepository");
        missingGraphRepository.Should().Throw<ArgumentNullException>()
            .WithParameterName("graphRepository");
        missingLogger.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task LinkAsync_WhenCanonicalBikeIsMissing_LogsWarningAndDoesNotCreateEdge()
    {
        var bikeModelRepository = new Mock<IBikeModelRepository>();
        var graphRepository = new Mock<IGraphRepository>();
        var logger = new Mock<ILogger<ManualBikeLinker>>();
        using var cancellation = new CancellationTokenSource();
        var manualId = Guid.NewGuid();
        bikeModelRepository
            .Setup(repository => repository.FindCanonicalAsync("Honda", "CBR600RR", 2024, cancellation.Token))
            .ReturnsAsync((BikeModel?)null);
        var sut = new ManualBikeLinker(bikeModelRepository.Object, graphRepository.Object, logger.Object);

        await sut.LinkAsync(manualId, "Honda", "CBR600RR", 2024, cancellation.Token);

        bikeModelRepository.Verify(
            repository => repository.FindCanonicalAsync("Honda", "CBR600RR", 2024, cancellation.Token),
            Times.Once);
        graphRepository.Verify(
            repository => repository.UpsertEdgeAsync(It.IsAny<GraphEdgeDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
        logger.Verify(
            entry => entry.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains("canonical bike model not found", StringComparison.Ordinal)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task LinkAsync_WhenCanonicalBikeIsFound_UpsertsExpectedEdgeWithCancellationToken()
    {
        var bikeModelRepository = new Mock<IBikeModelRepository>();
        var graphRepository = new Mock<IGraphRepository>();
        using var cancellation = new CancellationTokenSource();
        var manualId = Guid.NewGuid();
        var bikeModelId = Guid.NewGuid();
        var bikeModel = new BikeModel { Id = bikeModelId };
        bikeModelRepository
            .Setup(repository => repository.FindCanonicalAsync("Honda", "CBR600RR", 2024, cancellation.Token))
            .ReturnsAsync(bikeModel);
        var sut = new ManualBikeLinker(
            bikeModelRepository.Object,
            graphRepository.Object,
            NullLogger<ManualBikeLinker>.Instance);
        var beforeLink = DateTimeOffset.UtcNow;

        await sut.LinkAsync(manualId, "Honda", "CBR600RR", 2024, cancellation.Token);

        var afterLink = DateTimeOffset.UtcNow;
        graphRepository.Verify(
            repository => repository.UpsertEdgeAsync(
                It.Is<GraphEdgeDto>(edge =>
                    edge.FromNodeId == manualId &&
                    edge.ToNodeId == bikeModelId &&
                    edge.RelationshipType == "manual-for-bike" &&
                    edge.Weight == 1.0 &&
                    edge.Context == null &&
                    edge.CreatedAtUtc >= beforeLink &&
                    edge.CreatedAtUtc <= afterLink),
                cancellation.Token),
            Times.Once);
    }
}
