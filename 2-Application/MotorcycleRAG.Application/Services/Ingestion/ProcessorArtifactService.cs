using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Coordinates processor artifact storage, search-chunk indexing, source retrieval, and job-stage reporting.
/// </summary>
public sealed class ProcessorArtifactService : IProcessorArtifactService
{
    private readonly IBlobStorageService _blobStorageService;
    private readonly BlobStorageOptions _blobStorageOptions;
    private readonly IChunkIndexingService _chunkIndexingService;
    private readonly IIngestionJobRepository _jobRepository;
    private readonly IIndexedArtifactRepository _artifactRepository;
    private readonly IIndexedChunkRepository _chunkRepository;
    private readonly IIngestionSourceAccessTokenService _sourceAccessTokenService;
    private readonly IIngestionJobService _ingestionJobService;
    private readonly ILogger<ProcessorArtifactService> _logger;

    public ProcessorArtifactService(
        IBlobStorageService blobStorageService,
        IOptions<BlobStorageOptions> blobStorageOptions,
        IChunkIndexingService chunkIndexingService,
        IIngestionJobRepository jobRepository,
        IIndexedArtifactRepository artifactRepository,
        IIndexedChunkRepository chunkRepository,
        IIngestionSourceAccessTokenService sourceAccessTokenService,
        IIngestionJobService ingestionJobService,
        ILogger<ProcessorArtifactService> logger)
    {
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _blobStorageOptions = blobStorageOptions?.Value ?? throw new ArgumentNullException(nameof(blobStorageOptions));
        _chunkIndexingService = chunkIndexingService ?? throw new ArgumentNullException(nameof(chunkIndexingService));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _artifactRepository = artifactRepository ?? throw new ArgumentNullException(nameof(artifactRepository));
        _chunkRepository = chunkRepository ?? throw new ArgumentNullException(nameof(chunkRepository));
        _sourceAccessTokenService = sourceAccessTokenService ?? throw new ArgumentNullException(nameof(sourceAccessTokenService));
        _ingestionJobService = ingestionJobService ?? throw new ArgumentNullException(nameof(ingestionJobService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ProcessorArtifactSourceResult> DownloadSourceAsync(
        string uploadId,
        string documentType,
        string? accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(uploadId) || !Guid.TryParse(uploadId, out _))
        {
            return new(null, null, ProcessorArtifactOperationStatus.InvalidUploadId);
        }

        if (!TryGetSourceContentType(documentType, out var contentType))
        {
            return new(null, null, ProcessorArtifactOperationStatus.InvalidDocumentType);
        }

        if (accessToken is not null &&
            (string.IsNullOrWhiteSpace(accessToken) || !_sourceAccessTokenService.IsValid(accessToken, uploadId, documentType)))
        {
            return new(null, null, ProcessorArtifactOperationStatus.Unauthorized);
        }

        var blobName = IngestionBlobPaths.BuildRawUploadBlobName(uploadId, documentType);
        var container = _blobStorageOptions.RawUploadsContainer;
        if (!await _blobStorageService.ExistsAsync(container, blobName, cancellationToken).ConfigureAwait(false))
        {
            return new(null, null, ProcessorArtifactOperationStatus.NotFound);
        }

        var stream = await _blobStorageService.DownloadAsync(container, blobName, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Serving ingestion source. UploadId={UploadId}, DocumentType={DocumentType}.",
            uploadId,  // codeql[cs/log-forging]
            documentType);  // codeql[cs/log-forging]
        return new(stream, contentType, ProcessorArtifactOperationStatus.Success);
    }

    /// <inheritdoc />
    public async Task<ProcessorArtifactUploadResult> UploadArtifactAsync(
        ProcessorArtifactUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Content.CanSeek)
        {
            request.Content.Position = 0;
        }

        if (string.IsNullOrWhiteSpace(request.UploadId) || !Guid.TryParse(request.UploadId, out _))
        {
            return new(null, ProcessorArtifactOperationStatus.InvalidUploadId);
        }

        if (!TryGetArtifactLocation(request.UploadId, request.ArtifactType, out var container, out var blobPath))
        {
            return new(null, ProcessorArtifactOperationStatus.InvalidArtifactType);
        }

        if (string.Equals(request.ArtifactType, "graph-entities", StringComparison.OrdinalIgnoreCase))
        {
            container = _blobStorageOptions.RawUploadsContainer;
        }

        await _blobStorageService.UploadAsync(
            container,
            blobPath,
            request.Content,
            request.ContentType,
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Processor artifact accepted. UploadId={UploadId}, ArtifactType={ArtifactType}.",
            request.UploadId,  // codeql[cs/log-forging]
            request.ArtifactType);  // codeql[cs/log-forging]

        if (string.Equals(request.ArtifactType, "search-chunks", StringComparison.OrdinalIgnoreCase))
        {
            if (!request.Content.CanSeek)
            {
                throw new InvalidOperationException("Search chunk artifact content must be seekable for indexing.");
            }

            request.Content.Position = 0;
            await ProcessSearchChunksAsync(request.UploadId, container, blobPath, request.Content, cancellationToken).ConfigureAwait(false);
        }

        return new(
            new ProcessorArtifactUploadResponse
            {
                UploadId = request.UploadId,
                ArtifactType = request.ArtifactType,
                BlobPath = blobPath,
                Status = "stored"
            },
            ProcessorArtifactOperationStatus.Success);
    }

    /// <inheritdoc />
    public async Task<IngestionJobStatusResponse?> ReportJobStageByRunIdAsync(
        string runId,
        IngestionJobStageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(request);

        var job = await _jobRepository.GetByDocIngestionRunIdAsync(runId, cancellationToken).ConfigureAwait(false);
        return job is null
            ? null
            : await _ingestionJobService.TransitionStageAsync(job.IngestionJobId, request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IngestionJobStatusResponse> ReportJobStageAsync(
        Guid jobId,
        IngestionJobStageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _ingestionJobService.TransitionStageAsync(jobId, request, cancellationToken);
    }

    private async Task ProcessSearchChunksAsync(string uploadId, string container, string blobPath, Stream buffer, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var result = await _chunkIndexingService.IndexFromJsonlAsync(buffer, uploadId, cancellationToken).ConfigureAwait(false);
            var expected = result.TotalParsed;
            var indexed = result.Outcomes.Count(static outcome => outcome.Succeeded);
            var failed = result.Outcomes.Count(static outcome => !outcome.Succeeded);
            var artifactState = ComputeArtifactState(indexed, expected);
            var job = await FindLatestSearchChunkJobAsync(uploadId, cancellationToken).ConfigureAwait(false);

            if (job is null)
            {
                _logger.LogWarning("No ingestion job found for uploadId {UploadId}. Skipping catalog write. Artifact is stored in blob.", uploadId);  // codeql[cs/log-forging]
            }
            else
            {
                var artifact = new IndexedArtifactDto
                {
                    IndexedArtifactId = Guid.NewGuid(),
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

                var failureReason = failed > 0 ? $"Failed to index {failed} chunk(s)" : null;
                var terminalStatus = MapArtifactStateToJobStatus(artifactState);
                var transitioned = await TryTransitionSearchChunkJobToTerminalAsync(job.IngestionJobId, terminalStatus, expected, indexed, failureReason, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "Search chunk indexing terminal outcome: JobId={JobId}, Outcome={Outcome}, Transitioned={Transitioned}, Expected={ExpectedCount}, Indexed={IndexedCount}, Failed={FailedCount}, Batches={BatchCount}, DurationMs={DurationMs}, FailureReason={FailureReason}.",
                    job.IngestionJobId, terminalStatus, transitioned, expected, indexed, failed, result.BatchCount, stopwatch.ElapsedMilliseconds,
                    failureReason is null ? string.Empty : failureReason);
            }

            try
            {
                await _blobStorageService.SetMetadataAsync(
                    container,
                    blobPath,
                    new Dictionary<string, string>
                    {
                        ["state"] = artifactState.ToString(),
                        ["dateLastProcessed"] = now.UtcDateTime.ToString("O")
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception metadataException)
            {
                _logger.LogError(metadataException, "Best-effort blob metadata update failed for {UploadId}. Continuing.", uploadId);  // codeql[cs/log-forging]
            }
        }
        catch (Exception indexingException)
        {
            stopwatch.Stop();
            _logger.LogError(indexingException, "Chunk indexing into Azure AI Search failed for upload {UploadId} after {DurationMs}ms. Artifact is stored in blob.", uploadId, stopwatch.ElapsedMilliseconds);  // codeql[cs/log-forging]
            await TryFailSearchChunkJobAsync(uploadId, indexingException, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TryFailSearchChunkJobAsync(string uploadId, Exception failure, CancellationToken cancellationToken)
    {
        try
        {
            var job = await FindLatestSearchChunkJobAsync(uploadId, cancellationToken).ConfigureAwait(false);
            if (job is null)
            {
                return;
            }

            await TryTransitionSearchChunkJobToTerminalAsync(
                job.IngestionJobId,
                IngestionJobStatus.Failed,
                expectedChunkCount: 0,
                indexedChunkCount: 0,
                failureReason: BuildIndexingFailureReason(failure),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception transitionException)
        {
            _logger.LogError(transitionException, "Failed to transition ingestion job to Failed for upload {UploadId}. The artifact is stored in blob.", uploadId);  // codeql[cs/log-forging]
        }
    }

    private async Task<IngestionJob?> FindLatestSearchChunkJobAsync(string uploadId, CancellationToken cancellationToken)
    {
        var candidates = new List<IngestionJob>();
        foreach (var inputType in new[] { IngestionJobType.PDFManual, IngestionJobType.StructuredSpecification, IngestionJobType.Batch })
        {
            var candidate = await _jobRepository.GetLatestByInputAsync(uploadId, inputType, cancellationToken).ConfigureAwait(false);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        return candidates.OrderByDescending(static job => job.CreatedAtUtc).FirstOrDefault();
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
            if (await _jobRepository.TryTransitionToTerminalAsync(jobId, activeStatus, terminalStatus, expectedChunkCount, indexedChunkCount, failureReason, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetArtifactLocation(string uploadId, string artifactType, out string container, out string blobPath)
    {
        if (string.Equals(artifactType, "search-chunks", StringComparison.OrdinalIgnoreCase))
        {
            container = "search-chunks";
            blobPath = $"{uploadId}/chunks.jsonl";
            return true;
        }

        if (string.Equals(artifactType, "graph-entities", StringComparison.OrdinalIgnoreCase))
        {
            container = "graph-entities";
            blobPath = $"graph-entities/{uploadId}/entities.json";
            return true;
        }

        container = string.Empty;
        blobPath = string.Empty;
        return false;
    }

    private static bool TryGetSourceContentType(string documentType, out string contentType)
    {
        if (string.Equals(documentType, "manual-pdf", StringComparison.OrdinalIgnoreCase))
        {
            contentType = "application/pdf";
            return true;
        }

        if (string.Equals(documentType, "spec-dataset", StringComparison.OrdinalIgnoreCase))
        {
            contentType = "text/csv";
            return true;
        }

        contentType = string.Empty;
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
