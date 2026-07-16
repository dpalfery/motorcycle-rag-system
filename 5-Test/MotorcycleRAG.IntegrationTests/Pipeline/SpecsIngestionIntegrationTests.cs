using Moq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;
using Xunit;

namespace MotorcycleRAG.IntegrationTests.Pipeline;

/// <summary>
/// T031 — TDD-RED integration tests for CSV specs ingestion via SpecsIngestionService.
/// These tests compile but FAIL at runtime until SpecsIngestionService (T034) is implemented
/// and wired into the DI container.
/// Covers: valid CSV ingestion, duplicate rows, missing required columns, empty stream.
/// </summary>
public class SpecsIngestionIntegrationTests {
    private readonly Mock<IBikeModelRepository> _repositoryMock;
    private readonly Mock<ILogger<SpecsIngestionService>> _loggerMock;
    private readonly SpecsIngestionService _sut;

    public SpecsIngestionIntegrationTests() {
        _repositoryMock = new Mock<IBikeModelRepository>(MockBehavior.Strict);
        _loggerMock = new Mock<ILogger<SpecsIngestionService>>();
        _sut = new SpecsIngestionService(_repositoryMock.Object, _loggerMock.Object);
    }

    /// <summary>
    /// Valid CSV with 3 data rows should call UpsertAsync exactly 3 times,
    /// once per data row, with correctly parsed Make/Model/Year and normalized name.
    /// </summary>
    [Fact]
    public async Task IngestCsvAsync_ValidCsv3Rows_Calls3Upserts() {
        // Arrange
        const string csv = "Make,Model,Year\nHonda,CBR 1000RR,2023\nYamaha,YZF-R1,2022\nKawasaki,ZX-10R,2024\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var uploadId = "upload-001";
        var userId = "user-abc";

        _repositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<BikeModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _sut.IngestCsvAsync(uploadId, userId, stream);

        // Assert
        _repositoryMock.Verify(
            r => r.UpsertAsync(It.IsAny<BikeModel>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        // Verify first row was parsed correctly
        _repositoryMock.Verify(
            r => r.UpsertAsync(
                It.Is<BikeModel>(b =>
                    b.Make == "Honda" &&
                    b.Model == "CBR 1000RR" &&
                    b.Year == 2023 &&
                    b.NormalizedName == BikeModel.NormalizeName("Honda CBR 1000RR") &&
                    b.CreatedByUserId == userId &&
                    b.UploadRef == uploadId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Duplicate CSV rows should still call UpsertAsync for each row.
    /// Idempotent MERGE is the repository's responsibility, not the service's.
    /// </summary>
    [Fact]
    public async Task IngestCsvAsync_DuplicateRows_CallsUpsertPerRow() {
        // Arrange — same row repeated 3 times
        const string csv = "Make,Model,Year\nHonda,CBR 600RR,2023\nHonda,CBR 600RR,2023\nHonda,CBR 600RR,2023\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        _repositoryMock
            .Setup(r => r.UpsertAsync(It.IsAny<BikeModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await _sut.IngestCsvAsync("upload-dup", "user-xyz", stream);

        // Assert — 3 calls even though rows are identical (repo handles MERGE idempotency)
        _repositoryMock.Verify(
            r => r.UpsertAsync(It.IsAny<BikeModel>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    /// <summary>
    /// A CSV that is missing the required 'Year' column should skip all data rows.
    /// No UpsertAsync calls should be made.
    /// </summary>
    [Fact]
    public async Task IngestCsvAsync_MissingYearColumn_SkipsAllRows() {
        // Arrange — CSV has Make and Model but no Year column
        const string csv = "Make,Model\nHonda,CBR 1000RR\nYamaha,YZF-R1\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        // Act
        await _sut.IngestCsvAsync("upload-no-year", "user-123", stream);

        // Assert — no rows should be upserted because Year is required
        _repositoryMock.Verify(
            r => r.UpsertAsync(It.IsAny<BikeModel>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// An empty CSV stream (0 bytes) should complete without exception and make 0 UpsertAsync calls.
    /// </summary>
    [Fact]
    public async Task IngestCsvAsync_EmptyStream_NoCallsNoException() {
        // Arrange — empty stream
        using var stream = new MemoryStream();

        // Act
#pragma warning disable CA2025 // tasks are awaited before disposal scope ends
        var act = () => _sut.IngestCsvAsync("upload-empty", "user-000", stream);
#pragma warning restore CA2025

        // Assert — should not throw and should make zero upsert calls
        await act.Should().NotThrowAsync();
        _repositoryMock.Verify(
            r => r.UpsertAsync(It.IsAny<BikeModel>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}