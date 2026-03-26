using System.Text.Json;
using MotorcycleRAG.Application.Pipeline;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Application.Mapping;

/// <summary>
/// Maps <see cref="IngestionJob"/> domain entities to <see cref="IngestionJobStatusResponse"/> DTOs.
/// Supports FR-010 (status) and FR-010a (coverage reporting).
/// </summary>
public static class IngestionJobStatusMapper
{
    /// <summary>
    /// Maps an <see cref="IngestionJob"/> to its response DTO.
    /// Returns null if the input job is null, satisfying integration test requirements.
    /// </summary>
    /// <param name="job">The ingestion job entity to map.</param>
    /// <returns>A populated response DTO, or null if job is null.</returns>
    public static IngestionJobStatusResponse? Map(IngestionJob? job)
    {
        if (job is null)
        {
            return null;
        }

        var missingPages = new List<int>();
        if (!string.IsNullOrEmpty(job.MissingPagesJson))
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize<int[]>(job.MissingPagesJson);
                if (deserialized != null)
                {
                    missingPages.AddRange(deserialized);
                }
            }
            catch (JsonException)
            {
                // Malformed JSON — handle gracefully by returning empty list
            }
        }

        return new IngestionJobStatusResponse
        {
            JobId = job.IngestionJobId,
            Status = job.Status.ToString(),
            CreatedAtUtc = job.CreatedAtUtc,
            StartedAtUtc = job.StartedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc,
            InputType = job.InputType.ToString(),
            InputRef = job.InputRef,
            ManualDocumentId = job.ManualDocumentId,
            TotalPages = job.TotalPages,
            PagesCapturedViewableCount = job.PagesCapturedViewableCount,
            PagesWithSearchableTextCount = job.PagesWithSearchableTextCount,
            PagesWithOcrTextCount = job.PagesWithOcrTextCount,
            PagesWithNativeTextCount = job.PagesWithNativeTextCount,
            MissingPages = missingPages.AsReadOnly(),
            Coverage = CoverageCalculator.Calculate(job),
            FailureReason = job.FailureReason,
            FabricRunId = job.FabricRunId
        };
    }
}
