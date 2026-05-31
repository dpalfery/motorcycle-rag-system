using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Pipeline;

/// <summary>
/// Reprocesses chunk indexing artifacts that are in non-terminal or failed states.
/// Idempotent and handles continue-on-error for bulk operations.
/// </summary>
public sealed class ChunkReprocessService : IChunkReprocessService {
    private const string SearchChunksArtifactType = "search-chunks";

    private readonly IIndexedArtifactRepository _artifactRepository;
    private readonly IIndexedChunkRepository _chunkRepository;
    private readonly IIngestionJobRepository _jobRepository;
    private readonly IBlobStorageService _blobStorageService;
    private readonly IChunkIndexingService _indexingService;
    private readonly BlobStorageOptions _blobStorageOptions;
    private readonly IngestionOptions _ingestionOptions;
    private readonly ILogger<ChunkReprocessService> _logger;

    public ChunkReprocessService(
        IIndexedArtifactRepository artifactRepository,
        IIndexedChunkRepository chunkRepository,
        IIngestionJobRepository jobRepository,
        IBlobStorageService blobStorageService,
        IChunkIndexingService indexingService,
        IOptions<BlobStorageOptions> blobStorageOptions,
        IOptions<IngestionOptions> ingestionOptions,
        ILogger<ChunkReprocessService> logger) {
        _artifactRepository = artifactRepository ?? throw new ArgumentNullException(nameof(artifactRepository));
        _chunkRepository = chunkRepository ?? throw new ArgumentNullException(nameof(chunkRepository));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _indexingService = indexingService ?? throw new ArgumentNullException(nameof(indexingService));
        _blobStorageOptions = blobStorageOptions?.Value ?? throw new ArgumentNullException(nameof(blobStorageOptions));
        _ingestionOptions = ingestionOptions?.Value ?? throw new ArgumentNullException(nameof(ingestionOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ReprocessResultDto> ReprocessByJobIdAsync(Guid jobId, CancellationToken ct = default) {
        _logger.LogInformation("Starting reprocess for job {JobId}.", jobId);

        var job = await _jobRepository.GetByIdAsync(jobId, ct).ConfigureAwait(false);
        if (job is null) {
            _logger.LogWarning("Job {JobId} not found.", jobId);
            return new ReprocessResultDto(0, 0, 0, 0);
        }

        var artifact = await _artifactRepository.GetByUploadAndTypeAsync(
            job.InputRef,
            SearchChunksArtifactType,
            ct).ConfigureAwait(false);

        if (artifact is null) {
            _logger.LogWarning("Artifact of type {ArtifactType} not found for upload {UploadId}.", SearchChunksArtifactType, LogSanitizer.Sanitize(job.InputRef));
            return new ReprocessResultDto(0, 0, 0, 0);
        }

        var blobExists = await _blobStorageService.ExistsAsync(
            artifact.BlobContainer,
            artifact.BlobPath,
            ct).ConfigureAwait(false);

        if (!blobExists) {
            _logger.LogError(
                "Blob {BlobPath} in container {Container} does not exist. Marking artifact and job as failed.",
                LogSanitizer.Sanitize(artifact.BlobPath),
                LogSanitizer.Sanitize(artifact.BlobContainer));

            artifact.State = IndexedArtifactState.Failed;
            artifact.FailureReason = "Chunk artifact blob not found.";
            artifact.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _artifactRepository.UpsertAsync(artifact, ct).ConfigureAwait(false);

            await _jobRepository.UpdateStatusAsync(jobId, IngestionJobStatus.Failed, "Chunk artifact blob not found.", ct).ConfigureAwait(false);

            return new ReprocessResultDto(1, 0, 0, 1);
        }

        ChunkIndexingResult indexingResult;
        try {
            await using var stream = await _blobStorageService.DownloadAsync(
                artifact.BlobContainer,
                artifact.BlobPath,
                ct).ConfigureAwait(false);
            indexingResult = await _indexingService.IndexFromJsonlAsync(stream, job.InputRef, ct).ConfigureAwait(false);
        }
        catch (Exception ex) {
            _logger.LogError(
                ex,
                "Failed to download and index chunk artifact for job {JobId}.",
                jobId);

            artifact.State = IndexedArtifactState.Failed;
            artifact.FailureReason = $"Failed to download/index: {ex.Message}";
            artifact.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _artifactRepository.UpsertAsync(artifact, ct).ConfigureAwait(false);

            await _jobRepository.UpdateStatusAsync(jobId, IngestionJobStatus.Failed, "Failed to index chunks.", ct).ConfigureAwait(false);

            return new ReprocessResultDto(1, 0, 0, 1);
        }

        var indexedCount = indexingResult.Outcomes.Count(x => x.Succeeded);
        var failedCount = indexingResult.Outcomes.Count(x => !x.Succeeded);
        var expectedCount = indexingResult.TotalParsed;

        IndexedArtifactState newState = expectedCount switch {
            0 => IndexedArtifactState.Failed,
            var expected when indexedCount == expected => IndexedArtifactState.Completed,
            var expected when indexedCount == 0 => IndexedArtifactState.Failed,
            _ => IndexedArtifactState.PartiallyIndexed
        };

        artifact.State = newState;
        artifact.ExpectedChunkCount = expectedCount;
        artifact.IndexedChunkCount = indexedCount;
        artifact.FailedChunkCount = failedCount;
        artifact.LastProcessedAtUtc = DateTimeOffset.UtcNow;
        artifact.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (newState == IndexedArtifactState.Failed && indexedCount == 0) {
            artifact.FailureReason = "No chunks were successfully indexed.";
        }
        else {
            artifact.FailureReason = null;
        }

        await _artifactRepository.UpsertAsync(artifact, ct).ConfigureAwait(false);

        await _chunkRepository.DeleteByArtifactIdAsync(artifact.IndexedArtifactId, ct).ConfigureAwait(false);
        var indexedChunks = indexingResult.Outcomes
            .Where(o => o.Succeeded)
            .Select(o => new IndexedChunk {
                ChunkId = o.ChunkId,
                IndexedArtifactId = artifact.IndexedArtifactId,
                IngestionJobId = jobId,
                UploadId = job.InputRef,
                SourceFileName = o.SourceFile,
                PageNumber = o.PageNumber,
                ChunkIndex = o.ChunkIndex,
                Stage = "indexing",
                Status = ChunkIndexStatus.Complete,
                ProcessedAtUtc = DateTimeOffset.UtcNow
            })
            .ToList();

        if (indexedChunks.Count > 0) {
            await _chunkRepository.UpsertManyAsync(indexedChunks, ct).ConfigureAwait(false);
        }

        var terminalStatus = newState switch {
            IndexedArtifactState.Completed => IngestionJobStatus.Completed,
            IndexedArtifactState.PartiallyIndexed => IngestionJobStatus.PartiallyCompleted,
            _ => IngestionJobStatus.Failed
        };

        var failureReason = newState == IndexedArtifactState.Failed ? "Chunk indexing failed." : null;
        await _jobRepository.UpdateStatusAsync(jobId, terminalStatus, failureReason, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Reprocess completed for job {JobId}: expected={ExpectedCount}, indexed={IndexedCount}, failed={FailedCount}, state={NewState}.",
            jobId,
            expectedCount,
            indexedCount,
            failedCount,
            newState);

        var resultState = newState switch {
            IndexedArtifactState.Completed => 1,
            IndexedArtifactState.PartiallyIndexed => 1,
            _ => 1
        };

        return new ReprocessResultDto(
            ArtifactsProcessed: 1,
            ArtifactsSucceeded: newState == IndexedArtifactState.Completed ? 1 : 0,
            ArtifactsPartiallyIndexed: newState == IndexedArtifactState.PartiallyIndexed ? 1 : 0,
            ArtifactsFailed: newState == IndexedArtifactState.Failed ? 1 : 0);
    }

    /// <inheritdoc />
    public async Task<ReprocessResultDto> ReprocessAllNotSucceededAsync(CancellationToken ct = default) {
        _logger.LogInformation("Starting reprocess of all non-succeeded indexed artifacts.");

        var artifacts = await _artifactRepository.GetByStatesAsync(
            new[] { IndexedArtifactState.Pending, IndexedArtifactState.Indexing, IndexedArtifactState.PartiallyIndexed, IndexedArtifactState.Failed },
            ct).ConfigureAwait(false);

        return await ReprocessArtifactCollectionAsync(artifacts, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ReprocessResultDto> ReprocessAllAsync(CancellationToken ct = default) {
        _logger.LogInformation("Starting reprocess of all indexed artifacts.");

        var artifacts = await _artifactRepository.GetAllAsync(maxCount: 10000, ct).ConfigureAwait(false);

        return await ReprocessArtifactCollectionAsync(artifacts, ct).ConfigureAwait(false);
    }

    private async Task<ReprocessResultDto> ReprocessArtifactCollectionAsync(
        IReadOnlyList<IndexedArtifact> artifacts,
        CancellationToken ct) {
        var processed = 0;
        var succeeded = 0;
        var partiallyIndexed = 0;
        var failed = 0;

        foreach (var artifact in artifacts) {
            try {
                var result = await ReprocessByJobIdAsync(artifact.IngestionJobId, ct).ConfigureAwait(false);
                processed += result.ArtifactsProcessed;
                succeeded += result.ArtifactsSucceeded;
                partiallyIndexed += result.ArtifactsPartiallyIndexed;
                failed += result.ArtifactsFailed;
            }
            catch (Exception ex) {
                _logger.LogError(
                    ex,
                    "Error reprocessing artifact {ArtifactId} for job {JobId}. Continuing.",
                    artifact.IndexedArtifactId,
                    artifact.IngestionJobId);
            }
        }

        _logger.LogInformation(
            "Batch reprocess completed: processed={Processed}, succeeded={Succeeded}, partiallyIndexed={PartiallyIndexed}, failed={Failed}.",
            processed,
            succeeded,
            partiallyIndexed,
            failed);

        return new ReprocessResultDto(processed, succeeded, partiallyIndexed, failed);
    }
}
