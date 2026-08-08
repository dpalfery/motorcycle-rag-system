using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;
using MotorcycleRAG.Core.Utilities;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Coordinates search-chunk indexing with real anchors for an already-resolved ingestion job.
/// Handles indexing, persistence, job transition, and metadata stamping as a unified best-effort operation.
/// </summary>
public sealed class SearchChunkIndexingCoordinator : ISearchChunkIndexingCoordinator
{
    /// <summary>Metadata key names and values for blob storage (plan D5: orphan state machine).</summary>
    private static class BlobMetadata
    {
        public const string State = "state";
        public const string DateLastProcessed = "dateLastProcessed";
    }

    private readonly IChunkIndexingService _chunkIndexingService;
    private readonly IIndexedArtifactRepository _artifactRepository;
    private readonly IIndexedChunkRepository _chunkRepository;
    private readonly IIngestionJobRepository _jobRepository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IManualDocumentRepository _manualDocumentRepository;
    private readonly ILogger<SearchChunkIndexingCoordinator> _logger;

    public SearchChunkIndexingCoordinator(
        IChunkIndexingService chunkIndexingService,
        IIndexedArtifactRepository artifactRepository,
        IIndexedChunkRepository chunkRepository,
        IIngestionJobRepository jobRepository,
        IBlobStorageService blobStorageService,
        IManualDocumentRepository manualDocumentRepository,
        ILogger<SearchChunkIndexingCoordinator> logger)
    {
        _chunkIndexingService = chunkIndexingService ?? throw new ArgumentNullException(nameof(chunkIndexingService));
        _artifactRepository = artifactRepository ?? throw new ArgumentNullException(nameof(artifactRepository));
        _chunkRepository = chunkRepository ?? throw new ArgumentNullException(nameof(chunkRepository));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _manualDocumentRepository = manualDocumentRepository ?? throw new ArgumentNullException(nameof(manualDocumentRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SearchChunkIndexingOutcomeDto> IndexAsync(
        Stream jsonlStream,
        string uploadId,
        string container,
        string blobPath,
        Guid indexedArtifactId,
        IngestionJob job,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        // Capture the actual persisted state: if an exception occurs after persistence, we must report
        // what was actually written, not zeros (Issue 2 / plan D12 post-extraction behavior).
        int persistedExpected = 0;
        int persistedIndexed = 0;
        int persistedFailed = 0;
        IndexedArtifactState? persistedArtifactState = null;

        try
        {
            var now = DateTimeOffset.UtcNow;

            // Resolve sourceContentHash: null when no ManualDocument, never string.Empty
            var sourceContentHash = await SourceContentHashResolver.ResolveAsync(_manualDocumentRepository, job, cancellationToken).ConfigureAwait(false);

            // Call the 5-parameter overload with resolved anchors
            var result = await _chunkIndexingService.IndexFromJsonlAsync(
                jsonlStream,
                uploadId,
                indexedArtifactId,
                job.IngestionJobId,
                sourceContentHash,
                cancellationToken).ConfigureAwait(false);

            var expected = result.TotalParsed;
            var indexed = result.Outcomes.Count(static outcome => outcome.Succeeded);
            var failed = result.Outcomes.Count(static outcome => !outcome.Succeeded);
            var artifactState = ComputeArtifactState(indexed, expected);

            // Reuse the pre-allocated indexedArtifactId in artifact persistence
            var artifact = new IndexedArtifactDto
            {
                IndexedArtifactId = indexedArtifactId,
                IngestionJobId = job.IngestionJobId,
                UploadId = uploadId,
                ArtifactType = "search-chunks",
                BlobContainer = container,
                BlobPath = blobPath,
                State = artifactState,
                ExpectedChunkCount = expected,
                IndexedChunkCount = indexed,
                FailedChunkCount = failed,
                LastProcessedAtUtc = now,
                FailureReason = failed > 0 ? $"Failed to index {failed} chunk(s)" : null,
                CreatedAtUtc = now
            };

            await _artifactRepository.UpsertAsync(artifact, cancellationToken).ConfigureAwait(false);
            await _chunkRepository.DeleteByArtifactIdAsync(artifact.IndexedArtifactId, cancellationToken).ConfigureAwait(false);
            var indexedChunks = ConvertToIndexedChunks(result.Outcomes, artifact.IndexedArtifactId, job.IngestionJobId, uploadId, now);
            if (indexedChunks.Count > 0)
            {
                await _chunkRepository.UpsertManyAsync(indexedChunks, cancellationToken).ConfigureAwait(false);
            }

            // Capture persisted state for use in exception handlers
            persistedExpected = expected;
            persistedIndexed = indexed;
            persistedFailed = failed;
            persistedArtifactState = artifactState;

            var failureReason = failed > 0 ? $"Failed to index {failed} chunk(s)" : null;
            var terminalStatus = MapArtifactStateToJobStatus(artifactState);
            var transitioned = await TryTransitionSearchChunkJobToTerminalAsync(
                job.IngestionJobId,
                terminalStatus,
                expected,
                indexed,
                failureReason,
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Search chunk indexing terminal outcome: JobId={JobId}, Outcome={Outcome}, Transitioned={Transitioned}, Expected={ExpectedCount}, Indexed={IndexedCount}, Failed={FailedCount}, Batches={BatchCount}, DurationMs={DurationMs}, FailureReason={FailureReason}.",
                job.IngestionJobId, terminalStatus, transitioned, expected, indexed, failed, result.BatchCount, stopwatch.ElapsedMilliseconds,
                failureReason is null ? string.Empty : failureReason);

            try
            {
                await _blobStorageService.SetMetadataAsync(
                    container,
                    blobPath,
                    new Dictionary<string, string>
                    {
                        [BlobMetadata.State] = artifactState.ToString(),
                        [BlobMetadata.DateLastProcessed] = now.UtcDateTime.ToString("O")
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception metadataException)
            {
                _logger.LogError(metadataException, "Best-effort blob metadata update failed for {UploadId}. Continuing.", LogSanitizer.Sanitize(uploadId));  // codeql[cs/log-forging]
            }

            return new SearchChunkIndexingOutcomeDto(
                IndexedArtifactId: indexedArtifactId,
                IngestionJobId: job.IngestionJobId,
                Succeeded: true,
                ExpectedChunkCount: expected,
                IndexedChunkCount: indexed,
                FailedChunkCount: failed,
                ArtifactState: artifactState,
                JobTransitionedToTerminal: transitioned,
                FailureReason: null);
        }
        catch (Exception indexingException)
        {
            stopwatch.Stop();
            _logger.LogError(indexingException, "Chunk indexing into Azure AI Search failed for upload {UploadId} after {DurationMs}ms. Artifact is stored in blob.", LogSanitizer.Sanitize(uploadId), stopwatch.ElapsedMilliseconds);  // codeql[cs/log-forging]

            // Try to transition the job to Failed, but swallow exceptions (best-effort)
            var failureReason = BuildIndexingFailureReason(indexingException);
            await TryFailSearchChunkJobAsync(job.IngestionJobId, failureReason, cancellationToken).ConfigureAwait(false);

            // If artifact/chunks were persisted before the exception (e.g., job-transition threw),
            // report the actual persisted state. Only report zeros if the exception occurred before persistence.
            return new SearchChunkIndexingOutcomeDto(
                IndexedArtifactId: indexedArtifactId,
                IngestionJobId: job.IngestionJobId,
                Succeeded: false,
                ExpectedChunkCount: persistedExpected,
                IndexedChunkCount: persistedIndexed,
                FailedChunkCount: persistedFailed,
                ArtifactState: persistedArtifactState,
                JobTransitionedToTerminal: false,
                FailureReason: failureReason);
        }
    }

    private async Task TryFailSearchChunkJobAsync(Guid jobId, string failureReason, CancellationToken cancellationToken)
    {
        try
        {
            await TryTransitionSearchChunkJobToTerminalAsync(
                jobId,
                IngestionJobStatus.Failed,
                expectedChunkCount: 0,
                indexedChunkCount: 0,
                failureReason: failureReason,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception transitionException)
        {
            _logger.LogError(transitionException, "Failed to transition ingestion job to Failed for IngestionJobId {JobId}. The artifact is stored in blob.", jobId);
        }
    }

    private async Task<bool> TryTransitionSearchChunkJobToTerminalAsync(
        Guid jobId,
        IngestionJobStatus terminalStatus,
        int expectedChunkCount,
        int indexedChunkCount,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        foreach (var activeStatus in new[] { IngestionJobStatus.Indexing, IngestionJobStatus.Processing, IngestionJobStatus.Queued })
        {
            if (await _jobRepository.TryTransitionToTerminalAsync(
                jobId,
                activeStatus,
                terminalStatus,
                expectedChunkCount,
                indexedChunkCount,
                failureReason,
                cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private static IndexedArtifactState ComputeArtifactState(int indexed, int expected) =>
        indexed == expected && expected > 0 ? IndexedArtifactState.Completed :
        indexed > 0 && indexed < expected ? IndexedArtifactState.PartiallyIndexed :
        indexed == 0 ? IndexedArtifactState.Failed : IndexedArtifactState.Pending;

    private static IngestionJobStatus MapArtifactStateToJobStatus(IndexedArtifactState state) => state switch
    {
        IndexedArtifactState.Completed => IngestionJobStatus.Completed,
        IndexedArtifactState.PartiallyIndexed => IngestionJobStatus.PartiallyCompleted,
        IndexedArtifactState.Failed => IngestionJobStatus.Failed,
        _ => IngestionJobStatus.Queued
    };

    private static IReadOnlyList<IndexedChunkDto> ConvertToIndexedChunks(
        IReadOnlyList<ChunkIndexOutcome> outcomes,
        Guid artifactId,
        Guid jobId,
        string uploadId,
        DateTimeOffset now) => outcomes.Select(outcome => new IndexedChunkDto
        {
            ChunkId = outcome.ChunkId,
            IndexedArtifactId = artifactId,
            IngestionJobId = jobId,
            UploadId = uploadId,
            SourceFileName = outcome.SourceFile,
            PageNumber = outcome.PageNumber,
            ChunkIndex = outcome.ChunkIndex,
            Stage = "index",
            Status = outcome.Succeeded ? ChunkIndexStatus.Complete : ChunkIndexStatus.Failed,
            ProcessedAtUtc = now,
            FailureReason = outcome.FailureReason
        }).ToList();

    private static string BuildIndexingFailureReason(Exception failure)
    {
        const int maxReasonLength = 500;
        var message = failure.Message.Trim();
        return $"{failure.GetType().Name}: {(message.Length > maxReasonLength ? message[..maxReasonLength] : message)}";
    }
}
