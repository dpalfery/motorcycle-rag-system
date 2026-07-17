using Moq;
using FluentAssertions;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using Xunit;
using MotorcycleRAG.Application.Features.Ingestion.Mappers;

namespace MotorcycleRAG.IntegrationTests.Pipeline;

/// <summary>
/// T011 — Integration tests for ingestion job status lifecycle.
/// Tests domain entity defaults, repository mock interactions, and coverage response mapping.
/// </summary>
public class IngestionJobStatusIntegrationTests {
    private readonly Mock<IIngestionJobRepository> _repositoryMock;

    public IngestionJobStatusIntegrationTests() {
        _repositoryMock = new Mock<IIngestionJobRepository>(MockBehavior.Strict);
    }

    /// <summary>
    /// Pure domain entity test: a freshly-created IngestionJob must default to Queued status.
    /// This test PASSES immediately — it validates the domain entity invariant.
    /// </summary>
    [Fact]
    public void Job_DefaultStatus_IsQueued() {
        // Arrange & Act
        var job = IngestionJob.Create(IngestionJobType.PDFManual, "manuals/test-guid/upload/sample.pdf", createdBySubject: null);

        // Assert
        job.Status.Should().Be(IngestionJobStatus.Queued);
        job.IngestionJobId.Should().NotBeEmpty();
        job.CreatedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        job.StartedAtUtc.Should().BeNull();
        job.CompletedAtUtc.Should().BeNull();
        job.FailureReason.Should().BeNull();
        job.DocIngestionRunId.Should().BeNull();
        job.TotalPages.Should().BeNull();
        job.PagesCapturedViewableCount.Should().BeNull();
    }

    /// <summary>
    /// Tests that a completed job retrieved from the repository can be mapped
    /// to an IngestionJobStatusResponse with correct coverage metrics.
    /// </summary>
    [Fact]
    public async Task CoverageResponse_MapsCorrectly_WhenJobIsComplete() {
        // Arrange — build a completed ingestion job with coverage data
        var jobId = Guid.NewGuid();
        var completedJob = IngestionJob.Rehydrate(
            id: 1,
            ingestionJobId: jobId,
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-30),
            completedAtUtc: DateTimeOffset.UtcNow,
            createdBySubject: null,
            status: IngestionJobStatus.Completed,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: IngestionJobType.PDFManual,
            inputRef: "manuals/abc123/upload/honda-cbr600rr.pdf",
            sourceFileName: null,
            computeProvider: "MicrosoftFabric",
            docIngestionRunId: "fabric-run-001",
            manualDocumentId: Guid.NewGuid(),
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

        _repositoryMock
            .Setup(r => r.GetByIdAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(completedJob);

        // Act — retrieve job from mock repository
        var retrievedJob = await _repositoryMock.Object.GetByIdAsync(jobId, CancellationToken.None);
        retrievedJob.Should().NotBeNull();

        // Act — map using the real mapper
        var response = IngestionJobStatusMapper.Map(retrievedJob);

        // Assert
        response.Should().NotBeNull();
        response!.Coverage.Should().NotBeNull();
        response.Coverage!.ViewablePagesPercent.Should().Be(99.0);
        response.Coverage.SearchableTextPagesPercent.Should().BeApproximately(91.67, 0.01);
        response.Coverage.MissingPagesCount.Should().Be(12);
    }

    /// <summary>
    /// Tests that when a job is not found (repository returns null),
    /// the status response mapping should produce null.
    /// </summary>
    [Fact]
    public async Task StatusResponse_IsNull_WhenJobNotFound() {
        // Arrange — repository returns null for unknown job
        var unknownJobId = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.GetByIdAsync(unknownJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionJob?)null);

        // Act — retrieve job from mock repository
        var retrievedJob = await _repositoryMock.Object.GetByIdAsync(unknownJobId, CancellationToken.None);

        // Act — map using the real mapper
        var response = IngestionJobStatusMapper.Map(retrievedJob);

        // Assert
        response.Should().BeNull("When repository returns null for a jobId, the mapper should return null.");
    }
}
