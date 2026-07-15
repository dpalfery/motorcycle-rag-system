using FluentAssertions;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domian.Tests.Domain;

public sealed class IngestionAndManualModelTests
{
    [Fact]
    public void IngestionJob_StartPauseResumeAndComplete_EnforcesLifecycle()
    {
        var job = new IngestionJob { InputRef = "uploads/manual.pdf", InputType = IngestionJobType.PDFManual };

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
        var job = new IngestionJob
        {
            Status = IngestionJobStatus.Failed,
            FailureReason = "processor failed",
            CurrentStage = "failed",
            MetadataJson = "{\"make\":\"Honda\"}",
            IndexedChunkCount = 10
        };

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
        var processing = new IngestionJob { Status = IngestionJobStatus.Processing };
        var act = () => processing.MarkDeleting();
        act.Should().Throw<InvalidOperationException>();

        var completed = new IngestionJob { Status = IngestionJobStatus.Completed };
        completed.MarkDeleting();
        completed.Status.Should().Be(IngestionJobStatus.Deleting);
    }

    [Fact]
    public void IngestionJob_ResumeFromMetadata_WhenNotAwaitingMetadata_RejectsResume()
    {
        // Arrange
        var job = new IngestionJob();

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
        var job = new IngestionJob();
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
        var job = new IngestionJob { Status = status };

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
        var job = new IngestionJob();
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
        var job = new IngestionJob { Status = status };

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
        var job = new IngestionJob { MetadataJson = "{\"make\":\"Honda\"}", ExpectedChunkCount = 20, IndexedChunkCount = 11 };
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
        var updatedAt = completedAt.AddMinutes(1);
        var job = new IngestionJob
        {
            Id = 42,
            IngestionJobId = Guid.NewGuid(),
            CreatedAtUtc = createdAt,
            CreatedBySubject = "user-oid",
            InputType = IngestionJobType.PDFManual,
            InputRef = "uploads/manual.pdf",
            ComputeProvider = "AdminLocalProcessor",
            DocIngestionRunId = "processor-run-7",
            ManualDocumentId = Guid.NewGuid(),
            JobId = "legacy-job-7",
            JobType = "manual-pdf",
            SourceFilePath = "uploads/manual.pdf",
            SourceFileName = "manual.pdf",
            StartTime = startedAt,
            EndTime = completedAt,
            UserId = "legacy-user",
            UserEmail = "admin@example.test",
            TotalRecordsProcessed = 20,
            RecordsIndexed = 18,
            RecordsFailed = 1,
            RecordsWithWarnings = 1,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            TotalPages = 12,
            PagesCapturedViewableCount = 11,
            PagesWithSearchableTextCount = 10,
            PagesWithOcrTextCount = 8,
            PagesWithNativeTextCount = 2,
            MissingPagesJson = "[12]",
            MetricsJson = "{\"elapsedSeconds\":900}",
            Status = IngestionJobStatus.Failed,
        };
        var persistedAuditSnapshot = new
        {
            job.Id,
            job.IngestionJobId,
            job.CreatedAtUtc,
            job.CreatedBySubject,
            job.InputType,
            job.InputRef,
            job.ComputeProvider,
            job.DocIngestionRunId,
            job.ManualDocumentId,
            job.JobId,
            job.JobType,
            job.SourceFilePath,
            job.SourceFileName,
            job.StartTime,
            job.EndTime,
            job.UserId,
            job.UserEmail,
            job.TotalRecordsProcessed,
            job.RecordsIndexed,
            job.RecordsFailed,
            job.RecordsWithWarnings,
            job.CreatedAt,
            job.UpdatedAt,
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
        var job = new IngestionJob { Status = status };

        // Act
        job.MarkDeleting();

        // Assert
        job.Status.Should().Be(IngestionJobStatus.Deleting);
    }

    [Fact]
    public void IngestionJob_RollbackDeletion_WhenDeleting_RestoresFailedStateWithBoundedReason()
    {
        // Arrange
        var job = new IngestionJob();
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
        var job = new IngestionJob();

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
        var job = new IngestionJob();
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
        var job = new IngestionJob();
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
        var job = new IngestionJob();
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
        var job = new IngestionJob();

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
        var job = new IngestionJob { MetadataJson = "{\"make\":\"Honda\"}" };

        // Act
        job.SetMetadata("{\"make\":\"Honda\",\"model\":\"CBR600RR\",\"year\":2024}");

        // Assert
        job.MetadataJson.Should().Be("{\"make\":\"Honda\",\"model\":\"CBR600RR\",\"year\":2024}");
    }

    [Fact]
    public void IngestionJob_UpdateStage_WhenCountAndTimestampAreNotReported_PreservesUnknownValues()
    {
        // Arrange
        var job = new IngestionJob { Status = IngestionJobStatus.Processing };

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
        var job = new IngestionJob();

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
        var job = new IngestionJob();

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
        var job = new IngestionJob();
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
        var job = new IngestionJob();
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
        var document = new ManualDocument { SourceFileName = "manual.pdf" };
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
        var document = new ManualDocument();
        var act = () => document.MarkFailed(" ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ManualDocument_CanonicalizeProcessAndComplete_RecordsLifecycleEvidence()
    {
        // Arrange
        var document = new ManualDocument();
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
        var document = new ManualDocument();

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
        var document = new ManualDocument();
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
        var document = new ManualDocument();

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
        var document = new ManualDocument();
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
        var document = new ManualDocument();

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
        var document = new ManualDocument();
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
        var document = new ManualDocument();
        document.MarkFailed("source validation failed");

        // Act
        var act = () => document.MarkCanonicalized();

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Failed*");
        document.LastFailure.Should().Be("source validation failed");
    }
}
