using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domian.Tests.Domain;

public sealed class IngestionAndManualModelTests
{
    [Fact]
    public void IngestionJob_WhenCreated_UsesQueuedStatusAndMicrosoftFabricProvider()
    {
        // Arrange
        var job = new IngestionJob();

        // Act
        var state = new { job.IngestionJobId, job.Status, job.InputRef, job.ComputeProvider, job.StartedAtUtc, job.CompletedAtUtc, job.CreatedAtUtc };

        // Assert
        state.IngestionJobId.Should().NotBe(Guid.Empty);
        state.Status.Should().Be(IngestionJobStatus.Queued);
        state.InputRef.Should().BeEmpty();
        state.ComputeProvider.Should().Be("MicrosoftFabric");
        state.StartedAtUtc.Should().BeNull();
        state.CompletedAtUtc.Should().BeNull();
        state.CreatedAtUtc.Should().BeAfter(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void IngestionJob_WhenProcessorReportsMetadataAndIndexCounts_RetainsOperationalState()
    {
        // Arrange
        var jobId = Guid.NewGuid();
        var manualId = Guid.NewGuid();
        var stageSetAt = DateTimeOffset.Parse("2026-07-12T12:00:00+00:00");

        // Act
        var job = new IngestionJob
        {
            IngestionJobId = jobId,
            InputType = IngestionJobType.PDFManual,
            InputRef = "uploads/upload-123",
            Status = IngestionJobStatus.AwaitingMetadata,
            MetadataJson = "{\"make\":\"Honda\"}",
            ManualDocumentId = manualId,
            TotalPages = 200,
            PagesCapturedViewableCount = 199,
            PagesWithSearchableTextCount = 198,
            ExpectedChunkCount = 300,
            IndexedChunkCount = 250,
            CurrentStage = "indexing",
            StageSetAtUtc = stageSetAt
        };

        // Assert
        job.Should().BeEquivalentTo(new
        {
            IngestionJobId = jobId,
            InputType = IngestionJobType.PDFManual,
            InputRef = "uploads/upload-123",
            Status = IngestionJobStatus.AwaitingMetadata,
            MetadataJson = "{\"make\":\"Honda\"}",
            ManualDocumentId = manualId,
            TotalPages = 200,
            PagesCapturedViewableCount = 199,
            PagesWithSearchableTextCount = 198,
            ExpectedChunkCount = 300,
            IndexedChunkCount = 250,
            CurrentStage = "indexing",
            StageSetAtUtc = stageSetAt
        });
    }

    [Fact]
    public void IngestionJob_WhenPipelineFinishesWithLegacyMetrics_RetainsAuditAndCoverageState()
    {
        // Arrange
        var startedAt = DateTimeOffset.Parse("2026-07-12T10:00:00+00:00");
        var completedAt = DateTimeOffset.Parse("2026-07-12T11:30:00+00:00");

        // Act
        var job = new IngestionJob
        {
            Id = 42,
            CreatedBySubject = "entra-subject-123",
            FailureReason = "Two pages could not be rendered.",
            DocIngestionRunId = "fabric-run-123",
            JobId = "legacy-job-123",
            JobType = "pdf-manual",
            SourceFilePath = "uploads/manual.pdf",
            SourceFileName = "manual.pdf",
            StartTime = startedAt,
            EndTime = completedAt,
            UserId = "admin-123",
            UserEmail = "admin@example.test",
            TotalRecordsProcessed = 350,
            RecordsIndexed = 348,
            RecordsFailed = 2,
            RecordsWithWarnings = 1,
            ErrorsJson = "[{\"page\":19}]",
            ErrorMessage = "Page rendering completed with errors.",
            CreatedAt = startedAt,
            UpdatedAt = completedAt,
            PagesWithOcrTextCount = 120,
            PagesWithNativeTextCount = 228,
            MissingPagesJson = "[19,20]",
            MetricsJson = "{\"durationSeconds\":5400}"
        };

        // Assert
        job.Should().BeEquivalentTo(new
        {
            Id = 42L,
            CreatedBySubject = "entra-subject-123",
            FailureReason = "Two pages could not be rendered.",
            DocIngestionRunId = "fabric-run-123",
            JobId = "legacy-job-123",
            JobType = "pdf-manual",
            SourceFilePath = "uploads/manual.pdf",
            SourceFileName = "manual.pdf",
            StartTime = startedAt,
            EndTime = completedAt,
            UserId = "admin-123",
            UserEmail = "admin@example.test",
            TotalRecordsProcessed = 350,
            RecordsIndexed = 348,
            RecordsFailed = 2,
            RecordsWithWarnings = 1,
            ErrorsJson = "[{\"page\":19}]",
            ErrorMessage = "Page rendering completed with errors.",
            CreatedAt = startedAt,
            UpdatedAt = completedAt,
            PagesWithOcrTextCount = 120,
            PagesWithNativeTextCount = 228,
            MissingPagesJson = "[19,20]",
            MetricsJson = "{\"durationSeconds\":5400}"
        });
    }

    [Fact]
    public void ManualDocument_WhenCreated_InitializesPendingCanonicalizationState()
    {
        // Arrange
        var document = new ManualDocument();

        // Act
        var state = new
        {
            document.DocumentId,
            document.SourceFileName,
            document.CanonicalBlobContainer,
            document.CanonicalBlobPath,
            document.CurrentStatus,
            document.UploadedAtUtc,
            document.LastSuccessfulRunId,
            document.LastFailure
        };

        // Assert
        state.DocumentId.Should().NotBe(Guid.Empty);
        state.SourceFileName.Should().BeEmpty();
        state.CanonicalBlobContainer.Should().BeEmpty();
        state.CanonicalBlobPath.Should().BeEmpty();
        state.CurrentStatus.Should().Be(ManualDocumentStatus.Pending);
        state.UploadedAtUtc.Should().BeAfter(DateTimeOffset.UnixEpoch);
        state.LastSuccessfulRunId.Should().BeNull();
        state.LastFailure.Should().BeNull();
    }

    [Fact]
    public void ManualDocument_WhenCanonicalizedAndProcessed_RetainsSourceAndRunState()
    {
        // Arrange
        var canonicalizedAt = DateTimeOffset.Parse("2026-07-12T10:00:00+00:00");
        var processedAt = DateTimeOffset.Parse("2026-07-12T11:00:00+00:00");

        // Act
        var document = new ManualDocument
        {
            CanonicalBlobUri = "https://storage.example.test/manuals/cbr600rr.pdf",
            SourceContentHash = "sha256:abc123",
            Make = "Honda",
            Model = "CBR600RR",
            Year = 2024,
            CanonicalizedAtUtc = canonicalizedAt,
            LastProcessedAtUtc = processedAt,
            CurrentStage = "indexing"
        };

        // Assert
        document.CanonicalBlobUri.Should().Be("https://storage.example.test/manuals/cbr600rr.pdf");
        document.SourceContentHash.Should().Be("sha256:abc123");
        document.Make.Should().Be("Honda");
        document.Model.Should().Be("CBR600RR");
        document.Year.Should().Be(2024);
        document.CanonicalizedAtUtc.Should().Be(canonicalizedAt);
        document.LastProcessedAtUtc.Should().Be(processedAt);
        document.CurrentStage.Should().Be("indexing");
    }

    [Fact]
    public void ManualPage_WhenConfigured_RetainsRenderedPageDetails()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-07-12T12:00:00+00:00");

        // Act
        var page = new ManualPage
        {
            ManualDocumentId = documentId,
            PageNumber = 12,
            BlobKey = "manuals/manual-123/pages/12.png",
            ExtractedText = "Adjust the chain slack before riding.",
            IsOcrExtracted = true,
            CreatedAtUtc = createdAt
        };

        // Assert
        page.ManualPageId.Should().NotBe(Guid.Empty);
        page.ManualDocumentId.Should().Be(documentId);
        page.PageNumber.Should().Be(12);
        page.BlobKey.Should().Be("manuals/manual-123/pages/12.png");
        page.ExtractedText.Should().Be("Adjust the chain slack before riding.");
        page.IsOcrExtracted.Should().BeTrue();
        page.CreatedAtUtc.Should().Be(createdAt);
    }

    [Fact]
    public void ManualProcessingRun_WhenCompleted_RetainsRunMetrics()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var completedAt = DateTimeOffset.Parse("2026-07-12T12:00:00+00:00");

        // Act
        var run = new ManualProcessingRun
        {
            DocumentId = documentId,
            RunType = "full",
            Status = ManualRunStatus.Succeeded,
            CompletedAtUtc = completedAt,
            CompletedStage = "completed",
            ChunkCount = 120,
            GraphEntityCount = 30,
            GraphRelationCount = 45,
            VectorCount = 120
        };

        // Assert
        run.RunId.Should().NotBe(Guid.Empty);
        run.DocumentId.Should().Be(documentId);
        run.RunType.Should().Be("full");
        run.Status.Should().Be(ManualRunStatus.Succeeded);
        run.CompletedAtUtc.Should().Be(completedAt);
        run.CompletedStage.Should().Be("completed");
        run.ChunkCount.Should().Be(120);
        run.GraphEntityCount.Should().Be(30);
        run.GraphRelationCount.Should().Be(45);
        run.VectorCount.Should().Be(120);
    }

    [Fact]
    public void ManualProcessingRun_WhenRestartingFromChunking_RetainsExecutionContext()
    {
        // Arrange
        var documentId = Guid.NewGuid();

        // Act
        var run = new ManualProcessingRun
        {
            DocumentId = documentId,
            StartedFromStage = "chunking",
            LocalWorkingFolder = "/tmp/manual-123",
            ProcessorHost = "processor.example.test",
            ErrorSummary = "Embedding endpoint was unavailable."
        };

        // Assert
        run.DocumentId.Should().Be(documentId);
        run.StartedFromStage.Should().Be("chunking");
        run.LocalWorkingFolder.Should().Be("/tmp/manual-123");
        run.ProcessorHost.Should().Be("processor.example.test");
        run.ErrorSummary.Should().Be("Embedding endpoint was unavailable.");
        run.Status.Should().Be(ManualRunStatus.Started);
    }

    [Fact]
    public void ManualProcessingStage_WhenFailed_RetainsArtifactAndFailureDetails()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var completedAt = DateTimeOffset.Parse("2026-07-12T12:00:00+00:00");

        // Act
        var stage = new ManualProcessingStage
        {
            RunId = runId,
            StageName = "embedding",
            Status = ManualStageStatus.Failed,
            CompletedAtUtc = completedAt,
            ArtifactPath = "work/chunks.jsonl",
            ArtifactHash = "abc123",
            MetadataJson = "{\"chunks\":120}",
            ErrorDetail = "The configured embedding endpoint was unavailable."
        };

        // Assert
        stage.StageId.Should().NotBe(Guid.Empty);
        stage.RunId.Should().Be(runId);
        stage.StageName.Should().Be("embedding");
        stage.Status.Should().Be(ManualStageStatus.Failed);
        stage.CompletedAtUtc.Should().Be(completedAt);
        stage.ArtifactPath.Should().Be("work/chunks.jsonl");
        stage.ArtifactHash.Should().Be("abc123");
        stage.MetadataJson.Should().Be("{\"chunks\":120}");
        stage.ErrorDetail.Should().Be("The configured embedding endpoint was unavailable.");
    }
}
