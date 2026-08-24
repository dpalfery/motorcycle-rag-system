using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
/// Service that reconciles orphaned search-chunks artifacts (blobs whose ingestion jobs are missing or failed).
/// Implements the sweep algorithm defined in plan 2026-08-03-processor-artifact-skip-observability §3 Steps 3-4.
/// </summary>
public sealed class OrphanedArtifactSweepService : IOrphanedArtifactSweepService
{
    /// <summary>Stable EventId for the "no ingestion job after max attempts" terminal transition (D8).</summary>
    private static readonly EventId NoIngestionJobAfterMaxAttemptsEventId = new(1003, "NoIngestionJobAfterMaxAttempts");

    /// <summary>Stable EventId for the "no ingestion job within retention window" terminal transition (D8).</summary>
    private static readonly EventId NoIngestionJobWithinRetentionWindowEventId = new(1004, "NoIngestionJobWithinRetentionWindow");

    /// <summary>Stable EventId for the "ingestion job ID is empty" anchor-unsatisfiable condition (mirroring ProcessorArtifactService D6).</summary>
    private static readonly EventId IngestionJobIdEmptyEventId = new(1005, "IngestionJobIdEmpty");

    private readonly IBlobStorageService _blobStorageService;
    private readonly ISearchChunkIndexingCoordinator _searchChunkIndexingCoordinator;
    private readonly IIngestionJobRepository _jobRepository;
    private readonly IngestionOptions _ingestionOptions;
    private readonly ILogger<OrphanedArtifactSweepService> _logger;

    public OrphanedArtifactSweepService(
        IBlobStorageService blobStorageService,
        ISearchChunkIndexingCoordinator searchChunkIndexingCoordinator,
        IIngestionJobRepository jobRepository,
        IOptions<IngestionOptions> ingestionOptions,
        ILogger<OrphanedArtifactSweepService> logger)
    {
        _blobStorageService = blobStorageService ?? throw new ArgumentNullException(nameof(blobStorageService));
        _searchChunkIndexingCoordinator = searchChunkIndexingCoordinator ?? throw new ArgumentNullException(nameof(searchChunkIndexingCoordinator));
        _jobRepository = jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
        _ingestionOptions = ingestionOptions?.Value ?? throw new ArgumentNullException(nameof(ingestionOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrphanedArtifactDto>> ListOrphansAsync(CancellationToken cancellationToken = default)
    {
        var orphans = new List<OrphanedArtifactDto>();

        // List all blobs in the search-chunks container with metadata
        var blobs = await _blobStorageService.ListAsync(ProcessorArtifactService.SearchChunksArtifact.ContainerName, cancellationToken).ConfigureAwait(false);

        foreach (var blob in blobs)
        {
            // Extract the state metadata key
            if (blob.Metadata == null || !blob.Metadata.TryGetValue(ProcessorArtifactService.BlobMetadata.State, out var state))
            {
                // No state key present or metadata is null: ignore entirely
                continue;
            }

            // Include both Orphaned and OrphanedTerminal states
            if (state != ProcessorArtifactService.BlobMetadata.OrphanState &&
                state != ProcessorArtifactService.BlobMetadata.OrphanedTerminalState)
            {
                continue;
            }

            // Parse uploadId from the blob path (before the first /)
            var uploadId = ExtractUploadIdFromPath(blob.Name);
            if (string.IsNullOrWhiteSpace(uploadId))
            {
                continue;
            }

            // Extract orphan metadata
            blob.Metadata.TryGetValue(ProcessorArtifactService.BlobMetadata.OrphanReason, out var orphanReason);

            int orphanAttempts = 0;
            if (blob.Metadata.TryGetValue(ProcessorArtifactService.BlobMetadata.OrphanAttempts, out var attemptsStr) &&
                int.TryParse(attemptsStr, out var attempts))
            {
                orphanAttempts = attempts;
            }

            DateTimeOffset? orphanFirstDetectedUtc = null;
            if (blob.Metadata.TryGetValue(ProcessorArtifactService.BlobMetadata.OrphanFirstDetectedUtc, out var firstDetectedStr) &&
                DateTimeOffset.TryParse(firstDetectedStr, out var detected))
            {
                orphanFirstDetectedUtc = detected;
            }

            var orphan = new OrphanedArtifactDto(
                uploadId,
                ProcessorArtifactService.SearchChunksArtifact.ContainerName,
                blob.Name,
                state,
                orphanReason,
                orphanAttempts,
                orphanFirstDetectedUtc);

            orphans.Add(orphan);
        }

        return orphans.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<OrphanSweepResultDto> RunSweepAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var orphansFound = 0;
        var healed = 0;
        var attemptsIncremented = 0;
        var terminalTransitions = 0;
        var errored = 0;

        // List all blobs in the search-chunks container with metadata
        var blobs = await _blobStorageService.ListAsync(ProcessorArtifactService.SearchChunksArtifact.ContainerName, cancellationToken).ConfigureAwait(false);

        foreach (var blob in blobs)
        {
            // Extract the state metadata key
            if (blob.Metadata == null || !blob.Metadata.TryGetValue(ProcessorArtifactService.BlobMetadata.State, out var state))
            {
                // No state key present or metadata is null: ignore entirely
                continue;
            }

            // If state is OrphanedTerminal: skip entirely (D8)
            if (state == ProcessorArtifactService.BlobMetadata.OrphanedTerminalState)
            {
                continue;
            }

            // If state is not Orphaned: ignore entirely
            if (state != ProcessorArtifactService.BlobMetadata.OrphanState)
            {
                continue;
            }

            // Process this Orphaned blob inside a per-blob try/catch
            try
            {
                orphansFound++;

                // Parse uploadId from the blob path (before the first /)
                var uploadId = ExtractUploadIdFromPath(blob.Name);
                if (string.IsNullOrWhiteSpace(uploadId))
                {
                    errored++;
                    continue;
                }

                // Resolve the job using the three-type pattern (PDFManual, StructuredSpecification, Batch)
                var job = await FindLatestSearchChunkJobAsync(uploadId, cancellationToken).ConfigureAwait(false);

                if (job is not null && job.IngestionJobId != Guid.Empty)
                {
                    // Job found with valid anchor: heal the artifact
                    await HealArtifactAsync(blob, uploadId, job, cancellationToken).ConfigureAwait(false);
                    healed++;
                }
                else
                {
                    // Job absent or anchor is empty: treat the same as job-absent (D1/D2).
                    // If job is not null but has empty IngestionJobId (corrupted persistence row),
                    // log this distinctly so we can track the data corruption in monitoring.
                    if (job is not null && job.IngestionJobId == Guid.Empty)
                    {
                        _logger.LogError(
                            IngestionJobIdEmptyEventId,
                            "Resolved ingestionJobId is Guid.Empty for upload {UploadId}. This is a bug. Treating as job-absent. Blob: {BlobPath}.",
                            LogSanitizer.Sanitize(uploadId),
                            blob.Name);  // codeql[cs/log-forging]
                    }

                    // Check retention window and attempt budget
                    var (incrementedAttempts, terminalCount) = await ProcessJobAbsentAsync(blob, now, cancellationToken).ConfigureAwait(false);
                    attemptsIncremented += incrementedAttempts;
                    terminalTransitions += terminalCount;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing orphaned blob {BlobPath}. Continuing to next blob.", blob.Name);
                errored++;
            }
        }

        return new OrphanSweepResultDto(orphansFound, healed, attemptsIncremented, terminalTransitions, errored);
    }

    private async Task HealArtifactAsync(
        BlobObjectDescriptor blob,
        string uploadId,
        IngestionJob job,
        CancellationToken cancellationToken)
    {
        // Download the blob
        using var stream = await _blobStorageService.DownloadAsync(
            ProcessorArtifactService.SearchChunksArtifact.ContainerName,
            blob.Name,
            cancellationToken).ConfigureAwait(false);

        // Generate a fresh indexedArtifactId and invoke the coordinator
        var indexedArtifactId = Guid.NewGuid();
        await _searchChunkIndexingCoordinator.IndexAsync(
            stream,
            uploadId,
            ProcessorArtifactService.SearchChunksArtifact.ContainerName,
            blob.Name,
            indexedArtifactId,
            job,
            cancellationToken).ConfigureAwait(false);

        // The coordinator's own happy-path metadata stamp overwrites the orphan keys
    }

    private async Task<(int AttemptsIncremented, int TerminalTransitions)> ProcessJobAbsentAsync(
        BlobObjectDescriptor blob,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var attemptsIncremented = 0;
        var terminalTransitions = 0;

        // Extract current attempt count and first detected time
        if (!blob.Metadata!.TryGetValue(ProcessorArtifactService.BlobMetadata.OrphanAttempts, out var attemptsStr) ||
            !int.TryParse(attemptsStr, out var currentAttempts))
        {
            currentAttempts = 0;
        }

        if (!blob.Metadata!.TryGetValue(ProcessorArtifactService.BlobMetadata.OrphanFirstDetectedUtc, out var firstDetectedStr) ||
            !DateTimeOffset.TryParse(firstDetectedStr, out var orphanFirstDetectedUtc))
        {
            orphanFirstDetectedUtc = now;
        }

        // Compute new attempt count
        var newAttempts = currentAttempts + 1;

        // Check retention window first (independent of attempt count)
        if (now - orphanFirstDetectedUtc >= _ingestionOptions.OrphanRetentionWindow)
        {
            // Terminal by retention window
            _logger.LogError(
                NoIngestionJobWithinRetentionWindowEventId,
                "Orphaned blob {BlobPath} exceeded retention window. UploadId cannot be resolved. Transitioning to terminal.",
                blob.Name);

            await WriteTerminalMetadataAsync(
                blob,
                newAttempts,
                orphanFirstDetectedUtc,
                now,
                "NoIngestionJobWithinRetentionWindow",
                cancellationToken).ConfigureAwait(false);

            terminalTransitions++;
        }
        else if (newAttempts >= _ingestionOptions.MaxOrphanRetryAttempts)
        {
            // Terminal by max attempts
            _logger.LogError(
                NoIngestionJobAfterMaxAttemptsEventId,
                "Orphaned blob {BlobPath} exceeded max retry attempts. Transitioning to terminal.",
                blob.Name);

            await WriteTerminalMetadataAsync(
                blob,
                newAttempts,
                orphanFirstDetectedUtc,
                now,
                "NoIngestionJobAfterMaxAttempts",
                cancellationToken).ConfigureAwait(false);

            terminalTransitions++;
        }
        else
        {
            // Increment attempts and rewrite full metadata set
            await WriteOrphanedMetadataAsync(
                blob,
                newAttempts,
                orphanFirstDetectedUtc,
                now,
                cancellationToken).ConfigureAwait(false);

            attemptsIncremented++;
        }

        return (attemptsIncremented, terminalTransitions);
    }

    private async Task WriteOrphanedMetadataAsync(
        BlobObjectDescriptor blob,
        int orphanAttempts,
        DateTimeOffset orphanFirstDetectedUtc,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>
        {
            [ProcessorArtifactService.BlobMetadata.State] = ProcessorArtifactService.BlobMetadata.OrphanState,
            [ProcessorArtifactService.BlobMetadata.OrphanReason] = ProcessorArtifactService.BlobMetadata.NoJobOrphanReason,
            [ProcessorArtifactService.BlobMetadata.OrphanAttempts] = orphanAttempts.ToString(),
            [ProcessorArtifactService.BlobMetadata.OrphanFirstDetectedUtc] = orphanFirstDetectedUtc.UtcDateTime.ToString("O"),
            [ProcessorArtifactService.BlobMetadata.DateLastProcessed] = now.UtcDateTime.ToString("O")
        };

        await _blobStorageService.SetMetadataAsync(
            ProcessorArtifactService.SearchChunksArtifact.ContainerName,
            blob.Name,
            metadata,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteTerminalMetadataAsync(
        BlobObjectDescriptor blob,
        int orphanAttempts,
        DateTimeOffset orphanFirstDetectedUtc,
        DateTimeOffset now,
        string orphanReason,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>
        {
            [ProcessorArtifactService.BlobMetadata.State] = ProcessorArtifactService.BlobMetadata.OrphanedTerminalState,
            [ProcessorArtifactService.BlobMetadata.OrphanReason] = orphanReason,
            [ProcessorArtifactService.BlobMetadata.OrphanAttempts] = orphanAttempts.ToString(),
            [ProcessorArtifactService.BlobMetadata.OrphanFirstDetectedUtc] = orphanFirstDetectedUtc.UtcDateTime.ToString("O"),
            [ProcessorArtifactService.BlobMetadata.DateLastProcessed] = now.UtcDateTime.ToString("O")
        };

        await _blobStorageService.SetMetadataAsync(
            ProcessorArtifactService.SearchChunksArtifact.ContainerName,
            blob.Name,
            metadata,
            cancellationToken).ConfigureAwait(false);
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

    private static string ExtractUploadIdFromPath(string blobPath)
    {
        var slashIndex = blobPath.IndexOf('/');
        if (slashIndex <= 0)
        {
            return string.Empty;
        }

        return blobPath.Substring(0, slashIndex);
    }
}
