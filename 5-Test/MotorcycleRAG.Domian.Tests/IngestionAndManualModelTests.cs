using FluentAssertions;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domian.Tests.Domain;

public sealed class IngestionAndManualModelTests
{
    [Fact]
    public void IngestionJob_StartPauseResumeAndComplete_EnforcesLifecycle()
    {
        var job = CreateTestableJob(inputType: IngestionJobType.PDFManual, inputRef: "uploads/manual.pdf");

        job.StartProcessing();
        job.Status.Should().Be(IngestionJobStatus.Processing);
        job.StartedAtUtc.Should().NotBeNull();

        job.PauseForMetadata("missing year", "needs-manual-metadata");
        job.Status.Should().Be(IngestionJobStatus.AwaitingMetadata);
        job.ResumeFromMetadata("resuming");
        job.Complete();

        job.Status.Should().Be(IngestionJobStatus.Completed);
        job.CompletedAtUtc.Should().NotBeNull();
        var act = () => job.Fail("late failure");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IngestionJob_QueueForRetry_ClearsTransientState()
    {
        var job = CreateTestableJob(
            status: IngestionJobStatus.Failed,
            failureReason: "processor failed",
            currentStage: "failed",
            metadataJson: "{\"make\":\"Honda\"}",
            indexedChunkCount: 10);

        job.QueueForRetry();

        job.Status.Should().Be(IngestionJobStatus.Queued);
        job.FailureReason.Should().BeNull();
        job.CurrentStage.Should().BeNull();
        job.MetadataJson.Should().BeNull();
        job.IndexedChunkCount.Should().BeNull();
    }

    [Fact]
    public void IngestionJob_DeletingOnlyAllowsTerminalOrQueuedStates()
    {
        var processing = CreateTestableJob(status: IngestionJobStatus.Processing);
        var act = () => processing.MarkDeleting();
        act.Should().Throw<InvalidOperationException>();

        var completed = CreateTestableJob(status: IngestionJobStatus.Completed);
        completed.MarkDeleting();
        completed.Status.Should().Be(IngestionJobStatus.Deleting);
    }

    [Fact]
    public void IngestionJob_ResumeFromMetadata_WhenNotAwaitingMetadata_RejectsResume()
    {
        // Arrange
        var job = CreateTestableJob();

        // Act
        var act = () => job.ResumeFromMetadata("chunking");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*awaiting metadata*");
    }

    [Fact]
    public void IngestionJob_UpdateStage_WhenQueued_StartsProcessingAndKeepsFirstExpectedChunkCount()
    {
        // Arrange
        var job = CreateTestableJob();
        var startedAt = DateTimeOffset.Parse("2026-07-15T12:00:00+00:00");
        var updatedAt = startedAt.AddMinutes(1);

        // Act
        job.UpdateStage("chunking", chunksProcessed: 5, totalChunks: 12, startedAt);
        job.UpdateStage("indexing", chunksProcessed: 9, totalChunks: 20, updatedAt);

        // Assert
        job.Status.Should().Be(IngestionJobStatus.Processing);
        job.StartedAtUtc.Should().Be(startedAt);
        job.CurrentStage.Should().Be("indexing");
        job.StageSetAtUtc.Should().Be(updatedAt);
        job.ExpectedChunkCount.Should().Be(12);
        job.IndexedChunkCount.Should().Be(9);
    }

    [Theory]
    [InlineData(IngestionJobStatus.Completed)]
    [InlineData(IngestionJobStatus.Failed)]
    [InlineData(IngestionJobStatus.Cancelled)]
    [InlineData(IngestionJobStatus.PartiallyCompleted)]
    [InlineData(IngestionJobStatus.Deleting)]
    public void IngestionJob_UpdateStage_WhenTerminal_RejectsStageUpdate(IngestionJobStatus status)
    {
        // Arrange
        var job = CreateTestableJob(status: status);

        // Act
        var act = () => job.UpdateStage("indexing", chunksProcessed: 1, totalChunks: 2);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{status}*");
    }

    [Fact]
    public void IngestionJob_Cancel_WhenProcessing_SetsTerminalCancellationState()
    {
        // Arrange
        var job = CreateTestableJob();
        job.StartProcessing(DateTimeOffset.Parse("2026-07-15T12:00:00+00:00"));
        var cancelledAt = DateTimeOffset.Parse("2026-07-15T12:15:00+00:00");

        // Act
        job.Cancel("Operator stopped the job.", cancelledAt);

        // Assert
        job.Status.Should().Be(IngestionJobStatus.Cancelled);
        job.CompletedAtUtc.Should().Be(cancelledAt);
        job.FailureReason.Should().Be("Operator stopped the job.");
        job.CurrentStage.Should().Be("cancelled");
        job.StageSetAtUtc.Should().Be(cancelledAt);
    }

    [Theory]
    [InlineData(IngestionJobStatus.Queued)]
    [InlineData(IngestionJobStatus.Processing)]
    [InlineData(IngestionJobStatus.Completed)]
    [InlineData(IngestionJobStatus.PartiallyCompleted)]
    [InlineData(IngestionJobStatus.Deleting)]
    public void IngestionJob_QueueForRetry_WhenStatusIsNotRetryable_RejectsRetry(IngestionJobStatus status)
    {
        // Arrange
        var job = CreateTestableJob(status: status);

        // Act
        var act = () => job.QueueForRetry();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*can be retried*");
    }

    [Fact]
    public void IngestionJob_QueueForRetry_WhenCancelled_ResetsLifecycleAndProcessorState()
    {
        // Arrange
        var job = CreateTestableJob(metadataJson: "{\"make\":\"Honda\"}", expectedChunkCount: 20, indexedChunkCount: 11);
        job.StartProcessing(DateTimeOffset.Parse("2026-07-15T12:00:00+00:00"));
        job.Cancel("Retry after processor restart.", DateTimeOffset.Parse("2026-07-15T12:30:00+00:00"));
        job.RecordFailure("worker disconnected\nretry is safe");

        // Act
        job.QueueForRetry();

        // Assert
        job.Status.Should().Be(IngestionJobStatus.Queued);
        job.StartedAtUtc.Should().BeNull();
        job.CompletedAtUtc.Should().BeNull();
        job.FailureReason.Should().BeNull();
        job.CurrentStage.Should().BeNull();
        job.StageSetAtUtc.Should().BeNull();
        job.ExpectedChunkCount.Should().BeNull();
        job.IndexedChunkCount.Should().BeNull();
        job.MetadataJson.Should().BeNull();
        job.ErrorMessage.Should().BeNull();
        job.ErrorsJson.Should().BeNull();
    }

    [Fact]
    public void IngestionJob_QueueForRetry_PreservesPersistedAuditAndCoverageEvidence()
    {
        // Arrange
        var createdAt = DateTimeOffset.Parse("2026-07-15T08:00:00+00:00");
        var startedAt = createdAt.AddMinutes(2);
        var completedAt = startedAt.AddMinutes(15);
        var ingestionJobId = Guid.NewGuid();
        var manualDocumentId = Guid.NewGuid();
        var job = IngestionJob.Rehydrate(
            id: 42,
            ingestionJobId: ingestionJobId,
            createdAtUtc: createdAt,
            startedAtUtc: startedAt,
            completedAtUtc: completedAt,
            createdBySubject: "user-oid",
            status: IngestionJobStatus.Failed,
            failureReason: null,
            errorsJson: null,
            errorMessage: null,
            inputType: IngestionJobType.PDFManual,
            inputRef: "uploads/manual.pdf",
            sourceFileName: "manual.pdf",
            computeProvider: "AdminLocalProcessor",
            docIngestionRunId: "processor-run-7",
            manualDocumentId: manualDocumentId,
            totalPages: 12,
            pagesCapturedViewableCount: 11,
            pagesWithSearchableTextCount: 10,
            pagesWithOcrTextCount: 8,
            pagesWithNativeTextCount: 2,
            missingPagesJson: "[12]",
            metricsJson: "{\"elapsedSeconds\":900}",
            expectedChunkCount: null,
            indexedChunkCount: null,
            currentStage: null,
            stageSetAtUtc: null,
            metadataJson: null);
        var persistedAuditSnapshot = new
        {
            job.Id,
            job.IngestionJobId,
            job.CreatedAtUtc,
            job.CreatedBySubject,
            job.InputType,
            job.InputRef,
            job.SourceFileName,
            job.ComputeProvider,
            job.DocIngestionRunId,
            job.ManualDocumentId,
            job.TotalPages,
            job.PagesCapturedViewableCount,
            job.PagesWithSearchableTextCount,
            job.PagesWithOcrTextCount,
            job.PagesWithNativeTextCount,
            job.MissingPagesJson,
            job.MetricsJson,
        };

        // Act
        job.QueueForRetry();

        // Assert
        job.Should().BeEquivalentTo(persistedAuditSnapshot);
        job.Status.Should().Be(IngestionJobStatus.Queued);
    }

    [Theory]
    [InlineData(IngestionJobStatus.Queued)]
    [InlineData(IngestionJobStatus.AwaitingMetadata)]
    [InlineData(IngestionJobStatus.Completed)]
    [InlineData(IngestionJobStatus.Failed)]
    [InlineData(IngestionJobStatus.Cancelled)]
    [InlineData(IngestionJobStatus.PartiallyCompleted)]
    public void IngestionJob_MarkDeleting_WhenStatusAllowsDeletion_MarksDeleting(IngestionJobStatus status)
    {
        // Arrange
        var job = CreateTestableJob(status: status);

        // Act
        job.MarkDeleting();

        // Assert
        job.Status.Should().Be(IngestionJobStatus.Deleting);
    }

    [Fact]
    public void IngestionJob_RollbackDeletion_WhenDeleting_RestoresFailedStateWithBoundedReason()
    {
        // Arrange
        var job = CreateTestableJob();
        job.MarkDeleting();
        var reason = new string('x', 2001);

        // Act
        job.RollbackDeletion(reason);

        // Assert
        job.Status.Should().Be(IngestionJobStatus.Failed);
        job.FailureReason.Should().HaveLength(2000);
    }

    [Fact]
    public void IngestionJob_RollbackDeletion_WhenNotDeleting_RejectsRollback()
    {
        // Arrange
        var job = CreateTestableJob();

        // Act
        var act = () => job.RollbackDeletion("delete did not start");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*deleting job*");
    }

    [Fact]
    public void IngestionJob_PauseForMetadata_WhenReasonIsMissing_UsesFallbackReasonAndCanResume()
    {
        // Arrange
        var job = CreateTestableJob();
        var pausedAt = DateTimeOffset.Parse("2026-07-15T12:00:00+00:00");

        // Act
        job.PauseForMetadata(null, "needs-manual-metadata", pausedAt);
        job.ResumeFromMetadata("chunking", pausedAt.AddMinutes(1));

        // Assert
        job.Status.Should().Be(IngestionJobStatus.Processing);
        job.CurrentStage.Should().Be("chunking");
        job.StageSetAtUtc.Should().Be(pausedAt.AddMinutes(1));
        job.FailureReason.Should().BeNull();
    }

    [Fact]
    public void IngestionJob_Complete_WhenPartial_RecordsPartialCompletionAtSuppliedTime()
    {
        // Arrange
        var job = CreateTestableJob();
        var completedAt = DateTimeOffset.Parse("2026-07-15T12:00:00+00:00");

        // Act
        job.Complete(completedAt, partial: true);

        // Assert
        job.Status.Should().Be(IngestionJobStatus.PartiallyCompleted);
        job.CompletedAtUtc.Should().Be(completedAt);
        job.FailureReason.Should().BeNull();
    }

    [Fact]
    public void IngestionJob_Fail_WhenReasonExceedsLimit_TruncatesAndPreventsFurtherTransitions()
    {
        // Arrange
        var job = CreateTestableJob();
        var failedAt = DateTimeOffset.Parse("2026-07-15T12:00:00+00:00");
        var reason = new string('x', 2001);

        // Act
        job.Fail(reason, failedAt);
        var act = () => job.Cancel();

        // Assert
        job.Status.Should().Be(IngestionJobStatus.Failed);
        job.CompletedAtUtc.Should().Be(failedAt);
        job.FailureReason.Should().HaveLength(2000);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IngestionJob_RecordFailure_WhenDetailContainsWhitespaceAndMultipleLines_SanitizesOperationalFailure()
    {
        // Arrange
        var job = CreateTestableJob();

        // Act
        job.RecordFailure("  processor disconnected  \n retry is safe ");

        // Assert
        job.ErrorsJson.Should().Be("processor disconnected  \n retry is safe");
        job.ErrorMessage.Should().Be("processor disconnected");
        job.FailureReason.Should().Be("  processor disconnected  \n retry is safe ");
    }

    [Fact]
    public void IngestionJob_SetMetadata_WhenAdminProvidesMetadata_StoresTheReplacementPayload()
    {
        // Arrange
        var job = CreateTestableJob(metadataJson: "{\"make\":\"Honda\"}");

        // Act
        job.SetMetadata("{\"make\":\"Honda\",\"model\":\"CBR600RR\",\"year\":2024}");

        // Assert
        job.MetadataJson.Should().Be("{\"make\":\"Honda\",\"model\":\"CBR600RR\",\"year\":2024}");
    }

    [Fact]
    public void IngestionJob_UpdateStage_WhenCountAndTimestampAreNotReported_PreservesUnknownValues()
    {
        // Arrange
        var job = CreateTestableJob(status: IngestionJobStatus.Processing);

        // Act
        job.UpdateStage("embedding", chunksProcessed: null, totalChunks: null);

        // Assert
        job.CurrentStage.Should().Be("embedding");
        job.StageSetAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        job.ExpectedChunkCount.Should().BeNull();
        job.IndexedChunkCount.Should().BeNull();
    }

    [Fact]
    public void IngestionJob_Fail_WhenReasonIsWithinLimit_UsesCurrentTimeAndFullReason()
    {
        // Arrange
        var job = CreateTestableJob();

        // Act
        job.Fail("processor returned an invalid artifact");

        // Assert
        job.Status.Should().Be(IngestionJobStatus.Failed);
        job.CompletedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        job.FailureReason.Should().Be("processor returned an invalid artifact");
    }

    [Fact]
    public void IngestionJob_Cancel_WhenNoReasonIsSupplied_UsesDefaultCancellationReason()
    {
        // Arrange
        var job = CreateTestableJob();

        // Act
        job.Cancel();

        // Assert
        job.Status.Should().Be(IngestionJobStatus.Cancelled);
        job.FailureReason.Should().Be("Cancelled by user.");
        job.CompletedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void IngestionJob_RecordFailure_WhenDetailExceedsLimit_PreservesFullOperationalDetailAndBoundsFailureReason()
    {
        // Arrange
        var job = CreateTestableJob();
        var detail = new string('x', 2001);

        // Act
        job.RecordFailure(detail);

        // Assert
        job.ErrorsJson.Should().Be(detail);
        job.ErrorMessage.Should().Be(detail);
        job.FailureReason.Should().HaveLength(2000);
    }

    [Fact]
    public void IngestionJob_RollbackDeletion_WhenReasonIsWithinLimit_PreservesTheReason()
    {
        // Arrange
        var job = CreateTestableJob();
        job.MarkDeleting();

        // Act
        job.RollbackDeletion("storage deletion failed");

        // Assert
        job.Status.Should().Be(IngestionJobStatus.Failed);
        job.FailureReason.Should().Be("storage deletion failed");
    }

    [Fact]
    public void ManualDocument_TransitionsToProcessedAndRejectsMutationAfterFailure()
    {
        var document = ManualDocument.Create(Guid.NewGuid(), "manual.pdf", "container", "path", "type");
        document.BeginProcessing("extracting");
        document.SetStage("indexing");
        var runId = Guid.NewGuid();
        document.MarkProcessed(runId);

        document.CurrentStatus.Should().Be(ManualDocumentStatus.Processed);
        document.LastSuccessfulRunId.Should().Be(runId);
        document.LastProcessedAtUtc.Should().NotBeNull();

        var act = () => document.MarkFailed("should not regress");
        act.Should().Throw<InvalidOperationException>();
        document.CurrentStatus.Should().Be(ManualDocumentStatus.Processed);
    }

    [Fact]
    public void ManualDocument_MarkFailedRequiresReason()
    {
        var document = ManualDocument.Create(Guid.NewGuid(), "test.pdf", "container", "path", "type");
        var act = () => document.MarkFailed(" ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ManualDocument_CanonicalizeProcessAndComplete_RecordsLifecycleEvidence()
    {
        // Arrange
        var document = ManualDocument.Create(Guid.NewGuid(), "test.pdf", "container", "path", "type");
        var canonicalizedAt = DateTimeOffset.Parse("2026-07-15T08:00:00+00:00");
        var completedAt = canonicalizedAt.AddMinutes(20);
        var runId = Guid.NewGuid();

        // Act
        document.MarkCanonicalized(canonicalizedAt);
        document.BeginProcessing("extracting-pages");
        document.SetStage("indexing-content");
        document.MarkProcessed(runId, completedAt);

        // Assert
        document.CurrentStatus.Should().Be(ManualDocumentStatus.Processed);
        document.CanonicalizedAtUtc.Should().Be(canonicalizedAt);
        document.CurrentStage.Should().Be("indexing-content");
        document.LastSuccessfulRunId.Should().Be(runId);
        document.LastProcessedAtUtc.Should().Be(completedAt);
        document.LastFailure.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ManualDocument_BeginProcessing_WhenStageIsMissing_RejectsTheTransition(string? stage)
    {
        // Arrange
        var document = ManualDocument.Create(Guid.NewGuid(), "test.pdf", "container", "path", "type");

        // Act
        var act = () => document.BeginProcessing(stage!);

        // Assert
        act.Should().Throw<ArgumentException>();
        document.CurrentStatus.Should().Be(ManualDocumentStatus.Pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualDocument_BeginProcessing_WhenDocumentIsTerminal_RejectsTheTransition(bool failed)
    {
        // Arrange
        var document = ManualDocument.Create(Guid.NewGuid(), "test.pdf", "container", "path", "type");
        if (failed)
        {
            document.MarkFailed("source validation failed");
        }
        else
        {
            document.MarkProcessed(Guid.NewGuid());
        }

        // Act
        var act = () => document.BeginProcessing("extracting-pages");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{document.CurrentStatus}*");
    }

    [Fact]
    public void ManualDocument_SetStage_WhenNotProcessing_RejectsTheChange()
    {
        // Arrange
        var document = ManualDocument.Create(Guid.NewGuid(), "test.pdf", "container", "path", "type");

        // Act
        var act = () => document.SetStage("indexing-content");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must be processing*");
        document.CurrentStage.Should().BeNull();
    }

    [Fact]
    public void ManualDocument_SetStage_WhenStageIsMissing_RejectsTheChangeAndPreservesCurrentStage()
    {
        // Arrange
        var document = ManualDocument.Create(Guid.NewGuid(), "test.pdf", "container", "path", "type");
        document.BeginProcessing("extracting-pages");

        // Act
        var act = () => document.SetStage(" ");

        // Assert
        act.Should().Throw<ArgumentException>();
        document.CurrentStage.Should().Be("extracting-pages");
    }

    [Fact]
    public void ManualDocument_MarkProcessed_WhenRunIdIsEmpty_RejectsTheCompletion()
    {
        // Arrange
        var document = ManualDocument.Create(Guid.NewGuid(), "test.pdf", "container", "path", "type");

        // Act
        var act = () => document.MarkProcessed(Guid.Empty);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithParameterName("runId");
        document.CurrentStatus.Should().Be(ManualDocumentStatus.Pending);
    }

    [Fact]
    public void ManualDocument_MarkCanonicalized_WhenNoTimestampIsSupplied_RecordsTheTransitionTime()
    {
        // Arrange
        var document = ManualDocument.Create(Guid.NewGuid(), "test.pdf", "container", "path", "type");
        var before = DateTimeOffset.UtcNow;

        // Act
        document.MarkCanonicalized();

        // Assert
        document.CurrentStatus.Should().Be(ManualDocumentStatus.Canonicalized);
        document.CanonicalizedAtUtc.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void ManualDocument_MarkCanonicalized_WhenFailed_RejectsFurtherLifecycleChanges()
    {
        // Arrange
        var document = ManualDocument.Create(Guid.NewGuid(), "test.pdf", "container", "path", "type");
        document.MarkFailed("source validation failed");

        // Act
        var act = () => document.MarkCanonicalized();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Failed*");
        document.LastFailure.Should().Be("source validation failed");
    }

    private static IngestionJob CreateTestableJob(
        IngestionJobStatus status = IngestionJobStatus.Queued,
        string? failureReason = null,
        string? currentStage = null,
        string? metadataJson = null,
        int? expectedChunkCount = null,
        int? indexedChunkCount = null,
        IngestionJobType inputType = IngestionJobType.PDFManual,
        string inputRef = "test-input") =>
        IngestionJob.Rehydrate(
            id: 0,
            ingestionJobId: Guid.NewGuid(),
            createdAtUtc: DateTimeOffset.UtcNow,
            startedAtUtc: null,
            completedAtUtc: null,
            createdBySubject: null,
            status: status,
            failureReason: failureReason,
            errorsJson: null,
            errorMessage: null,
            inputType: inputType,
            inputRef: inputRef,
            sourceFileName: null,
            computeProvider: "AdminLocalProcessor",
            docIngestionRunId: null,
            manualDocumentId: null,
            totalPages: null,
            pagesCapturedViewableCount: null,
            pagesWithSearchableTextCount: null,
            pagesWithOcrTextCount: null,
            pagesWithNativeTextCount: null,
            missingPagesJson: null,
            metricsJson: null,
            expectedChunkCount: expectedChunkCount,
            indexedChunkCount: indexedChunkCount,
            currentStage: currentStage,
            stageSetAtUtc: null,
            metadataJson: metadataJson);
}
