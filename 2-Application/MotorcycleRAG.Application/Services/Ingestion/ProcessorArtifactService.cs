using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Contracts.Models.DTOs.Ingestion;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Coordinates processor artifact storage, search-chunk indexing, source retrieval, and job-stage reporting.
/// </summary>
public sealed class ProcessorArtifactService : IProcessorArtifactService
{
    /// <summary>Stable EventId for the "no ingestion job found" anchor-unsatisfiable condition (D6).</summary>
    private static readonly EventId NoIngestionJobFoundEventId = new(1001, "NoIngestionJobFound");

    /// <summary>Stable EventId for the "ingestion job ID is empty" anchor-unsatisfiable condition (D6).</summary>
    private static readonly EventId IngestionJobIdEmptyEventId = new(1002, "IngestionJobIdEmpty");

    /// <summary>Metadata key names and values for blob storage (plan D5: orphan state machine).</summary>
    internal static class BlobMetadata
    {
        public const string State = "state";
        public const string OrphanReason = "orphanReason";
        public const string OrphanAttempts = "orphanAttempts";
        public const string OrphanFirstDetectedUtc = "orphanFirstDetectedUtc";
        public const string DateLastProcessed = "dateLastProcessed";

        public const string OrphanState = "Orphaned";
        public const string OrphanedTerminalState = "OrphanedTerminal";
        public const string NoJobOrphanReason = "NoIngestionJob";
        public const string ZeroAttemptsValue = "0";
    }

    /// <summary>Container and path constants for search-chunks artifacts.</summary>
    internal static class SearchChunksArtifact
    {
        public const string ContainerName = "search-chunks";
    }

    private readonly IBlobStorageService _blobStorageService;
    private readonly BlobStorageOptions _blobStorageOptions;
    private readonly ISearchChunkIndexingCoordinator _searchChunkIndexingCoordinator;
    private readonly IIngestionJobRepository _jobRepository;
    private readonly IIngestionSourceAccessTokenService _sourceAccessTokenService;
    private readonly IIngestionJobService _ingestionJobService;
    private readonly ILogger<ProcessorArtifactService> _logger;

    public ProcessorArtifactService(
        IBlobStorageService blobStorageService,
        IOptions<BlobStorageOptions> blobStorageOptions,
        ISearchChunkIndexingCoordinator searchChunkIndexingCoordinator,
        IIngestionJobRepository jobRepository,
        IIngestionSourceAccessTokenService sourceAccessTokenService,
        IIngestionJobService ingestionJobService,
        ILogger<ProcessorArtifactService> logger)
    {
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _blobStorageOptions = blobStorageOptions?.Value ?? throw new ArgumentNullException(nameof(blobStorageOptions));
        _searchChunkIndexingCoordinator = searchChunkIndexingCoordinator ?? throw new ArgumentNullException(nameof(searchChunkIndexingCoordinator));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
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
            LogSanitizer.Sanitize(uploadId),  // codeql[cs/log-forging]
            LogSanitizer.Sanitize(documentType));  // codeql[cs/log-forging]
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
            LogSanitizer.Sanitize(request.UploadId),  // codeql[cs/log-forging]
            LogSanitizer.Sanitize(request.ArtifactType));  // codeql[cs/log-forging]

        ProcessorArtifactOperationStatus resultStatus = ProcessorArtifactOperationStatus.Success;
        string responseStatus = "stored";

        if (string.Equals(request.ArtifactType, "search-chunks", StringComparison.OrdinalIgnoreCase))
        {
            if (!request.Content.CanSeek)
            {
                throw new InvalidOperationException("Search chunk artifact content must be seekable for indexing.");
            }

            request.Content.Position = 0;
            bool indexingWasSkipped = await ProcessSearchChunksAsync(request.UploadId, container, blobPath, request.Content, cancellationToken).ConfigureAwait(false);
            if (indexingWasSkipped)
            {
                resultStatus = ProcessorArtifactOperationStatus.IndexingSkipped;
                responseStatus = "stored-not-indexed";
            }
        }

        return new(
            new ProcessorArtifactUploadResponse
            {
                UploadId = request.UploadId,
                ArtifactType = request.ArtifactType,
                BlobPath = blobPath,
                Status = responseStatus
            },
            resultStatus);
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

    private async Task<bool> ProcessSearchChunksAsync(string uploadId, string container, string blobPath, Stream buffer, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // Plan D3: Resolve indexedArtifactId, ingestionJobId BEFORE calling the coordinator
        var indexedArtifactId = Guid.NewGuid();
        var job = await FindLatestSearchChunkJobAsync(uploadId, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            _logger.LogError(NoIngestionJobFoundEventId, "No ingestion job found for uploadId {UploadId}. Skipping chunk indexing. Artifact is stored in blob.", LogSanitizer.Sanitize(uploadId));  // codeql[cs/log-forging]

            // Stamp the artifact as orphaned since no job exists to anchor it (D5)
            try
            {
                await _blobStorageService.SetMetadataAsync(
                    container,
                    blobPath,
                    new Dictionary<string, string>
                    {
                        [BlobMetadata.State] = BlobMetadata.OrphanState,
                        [BlobMetadata.OrphanReason] = BlobMetadata.NoJobOrphanReason,
                        [BlobMetadata.OrphanAttempts] = BlobMetadata.ZeroAttemptsValue,
                        [BlobMetadata.OrphanFirstDetectedUtc] = now.UtcDateTime.ToString("O"),
                        [BlobMetadata.DateLastProcessed] = now.UtcDateTime.ToString("O")
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception metadataException)
            {
                _logger.LogError(metadataException, "Best-effort blob metadata update failed for {UploadId}. Continuing.", LogSanitizer.Sanitize(uploadId));  // codeql[cs/log-forging]
            }

            return true;  // Indexing was skipped
        }

        // Validate that the resolved IDs are not Guid.Empty (a logic error, never legitimate)
        if (job.IngestionJobId == Guid.Empty)
        {
            _logger.LogError(IngestionJobIdEmptyEventId, "Resolved ingestionJobId is Guid.Empty. This is a bug. Skipping chunk indexing for upload {UploadId}.", LogSanitizer.Sanitize(uploadId));  // codeql[cs/log-forging]
            return true;  // Indexing was skipped
        }

        // Delegate to the coordinator for all post-job-resolution indexing logic
        await _searchChunkIndexingCoordinator.IndexAsync(buffer, uploadId, container, blobPath, indexedArtifactId, job, cancellationToken).ConfigureAwait(false);

        return false;  // Indexing was not skipped (handled by coordinator)
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

    private static bool TryGetArtifactLocation(string uploadId, string artifactType, out string container, out string blobPath)
    {
        if (string.Equals(artifactType, "search-chunks", StringComparison.OrdinalIgnoreCase))
        {
            container = SearchChunksArtifact.ContainerName;
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

}
