using FluentAssertions;
using MotorcycleRAG.Application.Features.Ingestion.Mappers;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.UnitTests.Features.Ingestion.Mappers;

public sealed class IngestionJobStatusMapperTests
{
    [Fact]
    public void Map_WithNullJob_ReturnsNull()
    {
        // Act
        var result = IngestionJobStatusMapper.Map(null);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Map_WithCompleteSnakeCaseMetadata_MapsStatusCoverageAndParsedMetadata()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var manualId = Guid.NewGuid();
        var job = CreateJob(
            status: IngestionJobStatus.AwaitingMetadata,
            failureReason: "fallback reason",
            metadataJson: """{"make":" Honda ","model":"CBR600RR","year":2023,"category":"sport","tags":[" abs ","fi",4]}""",
            id: 42,
            ingestionJobId: jobId,
            manualDocumentId: manualId,
            errorsJson: "processor failure detail",
            totalPages: 10,
            pagesCapturedViewableCount: 9,
            pagesWithSearchableTextCount: 8,
            pagesWithOcrTextCount: 3,
            pagesWithNativeTextCount: 5,
            missingPagesJson: "[2, 7]");

        // Act
        var result = IngestionJobStatusMapper.Map(job);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(42);
        result.JobId.Should().Be(jobId);
        result.ManualDocumentId.Should().Be(manualId);
        result.Status.Should().Be(nameof(IngestionJobStatus.AwaitingMetadata));
        result.InputType.Should().Be(nameof(IngestionJobType.PDFManual));
        result.MissingPages.Should().Equal(2, 7);
        result.Coverage!.ViewablePagesPercent.Should().Be(90);
        result.Coverage.SearchableTextPagesPercent.Should().Be(80);
        result.Coverage.OcrTextPercent.Should().Be(30);
        result.Coverage.NativeTextPercent.Should().Be(50);
        result.FailureReason.Should().Be("processor failure detail");
        result.FailureDetail.Should().Be("processor failure detail");
        result.RequiresManualMetadata.Should().BeTrue();
        result.Make.Should().Be("Honda");
        result.Model.Should().Be("CBR600RR");
        result.Year.Should().Be(2023);
        result.Category.Should().Be("sport");
        result.Tags.Should().Equal("abs", "fi");
        result.FillRate.Should().Be(1);
        result.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Map_WithPascalCaseAndStringYearMetadata_UsesFallbackPropertiesAndManualStage()
    {
        // Arrange
        var job = CreateJob(
            currentStage: "NEEDS-MANUAL-METADATA",
            metadataJson: """{"Make":" Yamaha ","Model":" MT-07 ","Year":"2024","Category":" naked ","Tags":[" road ","",3]}""");

        // Act
        var result = IngestionJobStatusMapper.Map(job);

        // Assert
        result.Should().NotBeNull();
        result!.RequiresManualMetadata.Should().BeTrue();
        result.Make.Should().Be("Yamaha");
        result.Model.Should().Be("MT-07");
        result.Year.Should().Be(2024);
        result.Category.Should().Be("naked");
        result.Tags.Should().Equal("road");
        result.FillRate.Should().Be(1);
        result.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void Map_WithMalformedStoredJson_ReturnsSafeEmptyCollectionsAndFallbackFailureReason()
    {
        // Arrange
        var job = CreateJob(failureReason: "stored failure", metadataJson: "{not-json", missingPagesJson: "{not-json");

        // Act
        var result = IngestionJobStatusMapper.Map(job);

        // Assert
        result.Should().NotBeNull();
        result!.MissingPages.Should().BeEmpty();
        result.FailureReason.Should().Be("stored failure");
        result.FailureDetail.Should().BeNull();
        result.Make.Should().BeNull();
        result.Tags.Should().BeNull();
        result.FillRate.Should().BeNull();
        result.IsComplete.Should().BeNull();
    }

    [Fact]
    public void Map_WithNonObjectOrInvalidMetadataValues_NormalizesThemToIncompleteMetadata()
    {
        // Arrange
        var arrayJob = CreateJob(metadataJson: "[\"not-an-object\"]");
        var invalidValuesJob = CreateJob(
            metadataJson: """{"make":42,"model":"   ","year":"not-a-year","tags":[1," "]}""");

        // Act
        var arrayResult = IngestionJobStatusMapper.Map(arrayJob);
        var invalidValuesResult = IngestionJobStatusMapper.Map(invalidValuesJob);

        // Assert
        arrayResult!.Make.Should().BeNull();
        arrayResult.FillRate.Should().BeNull();
        invalidValuesResult!.Make.Should().BeNull();
        invalidValuesResult.Model.Should().BeNull();
        invalidValuesResult.Year.Should().BeNull();
        invalidValuesResult.Category.Should().BeNull();
        invalidValuesResult.Tags.Should().BeNull();
        invalidValuesResult.FillRate.Should().Be(0);
        invalidValuesResult.IsComplete.Should().BeFalse();
    }

    private static IngestionJob CreateJob(
        IngestionJobStatus status = IngestionJobStatus.Processing,
        string? failureReason = null,
        string? metadataJson = null,
        string? currentStage = null,
        long id = 1,
        Guid? ingestionJobId = null,
        Guid? manualDocumentId = null,
        string? errorsJson = null,
        int? totalPages = null,
        int? pagesCapturedViewableCount = null,
        int? pagesWithSearchableTextCount = null,
        int? pagesWithOcrTextCount = null,
        int? pagesWithNativeTextCount = null,
        string? missingPagesJson = null) =>
        IngestionJob.Rehydrate(
            id: id,
            ingestionJobId: ingestionJobId ?? Guid.NewGuid(),
            createdAtUtc: new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero),
            startedAtUtc: new DateTimeOffset(2026, 7, 12, 12, 1, 0, TimeSpan.Zero),
            completedAtUtc: new DateTimeOffset(2026, 7, 12, 12, 2, 0, TimeSpan.Zero),
            createdBySubject: null,
            status: status,
            failureReason: failureReason,
            errorsJson: errorsJson,
            errorMessage: null,
            inputType: IngestionJobType.PDFManual,
            inputRef: "uploads/manual.pdf",
            sourceFileName: "manual.pdf",
            computeProvider: "LocalProcessingService",
            docIngestionRunId: "run-123",
            manualDocumentId: manualDocumentId,
            totalPages: totalPages,
            pagesCapturedViewableCount: pagesCapturedViewableCount,
            pagesWithSearchableTextCount: pagesWithSearchableTextCount,
            pagesWithOcrTextCount: pagesWithOcrTextCount,
            pagesWithNativeTextCount: pagesWithNativeTextCount,
            missingPagesJson: missingPagesJson,
            metricsJson: null,
            expectedChunkCount: null,
            indexedChunkCount: null,
            currentStage: currentStage,
            stageSetAtUtc: null,
            metadataJson: metadataJson);
}
