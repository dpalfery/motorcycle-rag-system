using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Pipeline;

/// <summary>
/// Orchestrates the ingestion job lifecycle: creation, status retrieval, and cancellation.
/// Delegates persistence to <see cref="IIngestionJobRepository"/> and pipeline triggering
/// to <see cref="IFabricPipelineService"/>.
/// </summary>
public sealed class IngestionJobService : IIngestionJobService {
    private readonly IIngestionJobRepository _repository;
    private readonly IFabricPipelineService _fabricPipeline;
    private readonly FabricIngestionOptions _options;
    private readonly ILogger<IngestionJobService> _logger;

    public IngestionJobService(
        IIngestionJobRepository repository,
        IFabricPipelineService fabricPipeline,
        IOptions<FabricIngestionOptions> options,
        ILogger<IngestionJobService> logger) {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _fabricPipeline = fabricPipeline ?? throw new ArgumentNullException(nameof(fabricPipeline));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IngestionJobStatusResponse> StartJobAsync(
        IngestionJobStartRequest request,
        string userId,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var inputType = request.DocumentType switch {
            "manual-pdf" => IngestionJobType.PDFManual,
            "spec-dataset" => IngestionJobType.StructuredSpecification,
            _ => throw new ArgumentException($"Unsupported document type: '{request.DocumentType}'.", nameof(request))
        };

        var pipelineId = inputType switch {
            IngestionJobType.PDFManual => _options.PdfPipelineId,
            IngestionJobType.StructuredSpecification => _options.CsvPipelineId,
            _ => throw new InvalidOperationException($"No pipeline configured for input type '{inputType}'.")
        };

        var job = new IngestionJob {
            InputType = inputType,
            InputRef = request.UploadId,
            CreatedBySubject = userId,
            Status = IngestionJobStatus.Queued
        };

        job = await _repository.CreateAsync(job, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Ingestion job {JobId} created for input type {InputType}.",
            job.IngestionJobId,
            job.InputType);

        string fabricRunId;
        try {
            fabricRunId = await _fabricPipeline.TriggerPipelineAsync(
                request.UploadId,
                request.DocumentType,
                pipelineId,
                ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger.LogError(
                ex,
                "Failed to trigger Fabric pipeline for job {JobId}.",
                job.IngestionJobId);

            await _repository.UpdateStatusAsync(
                job.IngestionJobId,
                IngestionJobStatus.Failed,
                "Failed to trigger pipeline.",
                ct).ConfigureAwait(false);

            job.Status = IngestionJobStatus.Failed;
            job.FailureReason = "Failed to trigger pipeline.";
            return MapToResponse(job);
        }

        job.FabricRunId = fabricRunId;
        job.Status = IngestionJobStatus.Processing;
        job.StartedAtUtc = DateTimeOffset.UtcNow;

        await _repository.UpdateAsync(job, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Fabric pipeline triggered for job {JobId} with run {FabricRunId}.",
            job.IngestionJobId,
            fabricRunId);

        return MapToResponse(job);
    }

    /// <inheritdoc />
    public async Task<IngestionJobStatusResponse?> GetJobStatusAsync(
        Guid jobId,
        string userId,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var job = await _repository.GetByIdAsync(jobId, ct).ConfigureAwait(false);
        if (job is null)
            return null;

        return MapToResponse(job);
    }

    /// <inheritdoc />
    public async Task CancelJobAsync(
        Guid jobId,
        string userId,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var job = await _repository.GetByIdAsync(jobId, ct).ConfigureAwait(false);
        if (job is null)
            throw new InvalidOperationException($"Ingestion job '{jobId}' not found.");

        if (job.Status is IngestionJobStatus.Completed
            or IngestionJobStatus.Failed
            or IngestionJobStatus.Cancelled
            or IngestionJobStatus.PartiallyCompleted) {
            _logger.LogWarning(
                "Cannot cancel job {JobId} in terminal status {Status}.",
                jobId,
                job.Status);
            return;
        }

        await _repository.UpdateStatusAsync(
            jobId,
            IngestionJobStatus.Cancelled,
            "Cancelled by user.",
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Ingestion job {JobId} cancelled.",
            jobId);
    }

    /// <summary>Maps a domain <see cref="IngestionJob"/> to its response DTO.</summary>
    private static IngestionJobStatusResponse MapToResponse(IngestionJob job) {
        IReadOnlyList<int> missingPages = [];
        if (!string.IsNullOrEmpty(job.MissingPagesJson)) {
            try {
                missingPages = JsonSerializer.Deserialize<int[]>(job.MissingPagesJson) ?? [];
            } catch (JsonException) {
                // Malformed JSON — return empty list rather than throwing.
            }
        }

        return new IngestionJobStatusResponse {
            JobId = job.IngestionJobId,
            Status = job.Status.ToString(),
            CreatedAtUtc = job.CreatedAtUtc,
            StartedAtUtc = job.StartedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc,
            InputType = job.InputType.ToString(),
            ManualDocumentId = job.ManualDocumentId,
            TotalPages = job.TotalPages,
            PagesCapturedViewableCount = job.PagesCapturedViewableCount,
            PagesWithSearchableTextCount = job.PagesWithSearchableTextCount,
            PagesWithOcrTextCount = job.PagesWithOcrTextCount,
            PagesWithNativeTextCount = job.PagesWithNativeTextCount,
            MissingPages = missingPages,
            Coverage = CoverageCalculator.Calculate(job),
            WorkloadLimits = new IngestionWorkloadLimits {
                MaxPages = job.TotalPages ?? 0,
                // TODO: Source MaxInputBytes and MaxRuntimeMinutes from FabricIngestionOptions
                // once the options class is wired into this service via IOptions<FabricIngestionOptions>.
                MaxInputBytes = 0,
                MaxRuntimeMinutes = 0
            },
            FailureReason = job.FailureReason,
            FabricRunId = job.FabricRunId
        };
    }
}