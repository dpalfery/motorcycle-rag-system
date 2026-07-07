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
        var job = new IngestionJob {
            InputType = IngestionJobType.PDFManual,
            InputRef = "manuals/guid/upload/perfect-manual.pdf",
            Status = IngestionJobStatus.Completed,
            TotalPages = 100,
            PagesCapturedViewableCount = 100,
            PagesWithSearchableTextCount = 100,
            PagesWithOcrTextCount = 20,
            PagesWithNativeTextCount = 80,
            MissingPagesJson = "[]"
        };

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
        var job = new IngestionJob {
            InputType = IngestionJobType.PDFManual,
            InputRef = "manuals/guid/upload/honda-cbr600rr.pdf",
            Status = IngestionJobStatus.Completed,
            TotalPages = 1200,
            PagesCapturedViewableCount = 1188,
            PagesWithSearchableTextCount = 1100,
            PagesWithOcrTextCount = 300,
            PagesWithNativeTextCount = 800,
            MissingPagesJson = "[45, 67, 89, 120, 455, 678, 901, 1023, 1100, 1150, 1175, 1199]"
        };

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
        var job = new IngestionJob {
            InputType = IngestionJobType.PDFManual,
            InputRef = "manuals/guid/upload/failed-scan.pdf",
            Status = IngestionJobStatus.Failed,
            TotalPages = 100,
            PagesCapturedViewableCount = 0,
            PagesWithSearchableTextCount = 0,
            PagesWithOcrTextCount = 0,
            PagesWithNativeTextCount = 0,
            MissingPagesJson = null
        };

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
        var job = new IngestionJob {
            InputType = IngestionJobType.StructuredSpecification,
            InputRef = "uploads/guid/specs.csv",
            Status = IngestionJobStatus.Completed,
            TotalPages = null,
            PagesCapturedViewableCount = null,
            PagesWithSearchableTextCount = null
        };

        // Act
        var metrics = CoverageCalculator.Calculate(job);

        // Assert
        metrics.Should().BeNull(
            "When TotalPages is null, coverage cannot be computed and should return null");
    }
}
