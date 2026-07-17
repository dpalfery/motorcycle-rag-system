// T012 — Unit tests for CoverageCalculator.Calculate(IngestionJob).
using FluentAssertions;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using Xunit;

namespace MotorcycleRAG.UnitTests.Pipeline;

public class CoverageCalculatorTests {
    [Fact]
    public void Calculate_WhenAllPagesViewable_Returns100Percent() {
        // Arrange
        var job = IngestionJob.Rehydrate(
            id: 1,
            ingestionJobId: Guid.NewGuid(),
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: null,
            completedAtUtc: null,
            createdBySubject: null,
            status: IngestionJobStatus.Completed,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: IngestionJobType.PDFManual,
            inputRef: "manuals/guid/upload/perfect-manual.pdf",
            sourceFileName: null,
            computeProvider: "MicrosoftFabric",
            docIngestionRunId: null,
            manualDocumentId: null,
            totalPages: 100,
            pagesCapturedViewableCount: 100,
            pagesWithSearchableTextCount: 100,
            pagesWithOcrTextCount: 20,
            pagesWithNativeTextCount: 80,
            missingPagesJson: "[]",
            metricsJson: null,
            expectedChunkCount: null,
            indexedChunkCount: null,
            currentStage: null,
            stageSetAtUtc: null,
            metadataJson: null);

        // Act
        var metrics = CoverageCalculator.Calculate(job);

        // Assert
        metrics.Should().NotBeNull();
        metrics!.ViewablePagesPercent.Should().Be(100.0);
        metrics.NativeTextPercent.Should().Be(80.0);
        metrics.OcrTextPercent.Should().Be(20.0);
        metrics.SearchableTextPagesPercent.Should().Be(100.0);
        metrics.MissingPagesCount.Should().Be(0);
    }

    [Fact]
    public void Calculate_WithMissingPages_ReturnsCorrectPercent() {
        // Arrange
        var job = IngestionJob.Rehydrate(
            id: 1,
            ingestionJobId: Guid.NewGuid(),
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: null,
            completedAtUtc: null,
            createdBySubject: null,
            status: IngestionJobStatus.Completed,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: IngestionJobType.PDFManual,
            inputRef: "manuals/guid/upload/honda-cbr600rr.pdf",
            sourceFileName: null,
            computeProvider: "MicrosoftFabric",
            docIngestionRunId: null,
            manualDocumentId: null,
            totalPages: 1200,
            pagesCapturedViewableCount: 1188,
            pagesWithSearchableTextCount: 1100,
            pagesWithOcrTextCount: 300,
            pagesWithNativeTextCount: 800,
            missingPagesJson: "[45, 67, 89, 120, 455, 678, 901, 1023, 1100, 1150, 1175, 1199]",
            metricsJson: null,
            expectedChunkCount: null,
            indexedChunkCount: null,
            currentStage: null,
            stageSetAtUtc: null,
            metadataJson: null);

        // Act
        var metrics = CoverageCalculator.Calculate(job);

        // Assert
        metrics.Should().NotBeNull();
        metrics!.ViewablePagesPercent.Should().Be(99.0,
            "Math.Round(1188.0 / 1200.0 * 100, 2) == 99.0");
        metrics.NativeTextPercent.Should().BeApproximately(66.67, 0.01);
        metrics.OcrTextPercent.Should().Be(25.0);
        metrics.SearchableTextPagesPercent.Should().BeApproximately(91.67, 0.01,
            "Math.Round(1100.0 / 1200.0 * 100, 2) ≈ 91.67");
        metrics.MissingPagesCount.Should().Be(12);
    }

    [Fact]
    public void Calculate_WhenNoPagesCapured_Returns0Percent() {
        // Arrange
        var job = IngestionJob.Rehydrate(
            id: 1,
            ingestionJobId: Guid.NewGuid(),
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: null,
            completedAtUtc: null,
            createdBySubject: null,
            status: IngestionJobStatus.Failed,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: IngestionJobType.PDFManual,
            inputRef: "manuals/guid/upload/failed-scan.pdf",
            sourceFileName: null,
            computeProvider: "MicrosoftFabric",
            docIngestionRunId: null,
            manualDocumentId: null,
            totalPages: 100,
            pagesCapturedViewableCount: 0,
            pagesWithSearchableTextCount: 0,
            pagesWithOcrTextCount: 0,
            pagesWithNativeTextCount: 0,
            missingPagesJson: null,
            metricsJson: null,
            expectedChunkCount: null,
            indexedChunkCount: null,
            currentStage: null,
            stageSetAtUtc: null,
            metadataJson: null);

        // Act
        var metrics = CoverageCalculator.Calculate(job);

        // Assert
        metrics.Should().NotBeNull();
        metrics!.ViewablePagesPercent.Should().Be(0.0);
        metrics.NativeTextPercent.Should().Be(0.0);
        metrics.OcrTextPercent.Should().Be(0.0);
        metrics.SearchableTextPagesPercent.Should().Be(0.0);
        metrics.MissingPagesCount.Should().Be(0);
    }

    [Fact]
    public void Calculate_WhenTotalPagesIsNull_ReturnsNullCoverage() {
        // Arrange — non-PDF job or job where pipeline hasn't reported page counts
        var job = IngestionJob.Rehydrate(
            id: 1,
            ingestionJobId: Guid.NewGuid(),
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: null,
            completedAtUtc: null,
            createdBySubject: null,
            status: IngestionJobStatus.Completed,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: IngestionJobType.StructuredSpecification,
            inputRef: "uploads/guid/specs.csv",
            sourceFileName: null,
            computeProvider: "MicrosoftFabric",
            docIngestionRunId: null,
            manualDocumentId: null,
            totalPages: null,
            pagesCapturedViewableCount: null,
            pagesWithSearchableTextCount: null,
            pagesWithOcrTextCount: null,
            pagesWithNativeTextCount: null,
            missingPagesJson: null,
            metricsJson: null,
            expectedChunkCount: null,
            indexedChunkCount: null,
            currentStage: null,
            stageSetAtUtc: null,
            metadataJson: null);

        // Act
        var metrics = CoverageCalculator.Calculate(job);

        // Assert
        metrics.Should().BeNull(
            "When TotalPages is null, coverage cannot be computed and should return null");
    }
}
