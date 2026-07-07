using System.Text.Json;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Computes <see cref="IngestionCoverageMetrics"/> from an <see cref="IngestionJob"/>'s page counts.
/// Pure static utility — no side effects, no I/O.
/// </summary>
public static class CoverageCalculator {
    /// <summary>
    /// Calculates coverage percentages and missing-page count for a completed ingestion job.
    /// Returns null when <see cref="IngestionJob.TotalPages"/> is null or zero (coverage cannot be computed).
    /// </summary>
    public static IngestionCoverageMetrics? Calculate(IngestionJob job) {
        if (job is null || job.TotalPages is null or 0)
            return null;

        var total = (double)job.TotalPages.Value;

        var missingPagesCount = 0;
        if (!string.IsNullOrEmpty(job.MissingPagesJson)) {
            try {
                var pages = JsonSerializer.Deserialize<int[]>(job.MissingPagesJson);
                missingPagesCount = pages?.Length ?? 0;
            } catch (JsonException) {
                // Malformed JSON — treat as zero missing pages rather than throwing.
                missingPagesCount = 0;
            }
        }

        return new IngestionCoverageMetrics {
            ViewablePagesPercent = Math.Round((job.PagesCapturedViewableCount ?? 0) / total * 100, 2),
            NativeTextPercent = Math.Round((job.PagesWithNativeTextCount ?? 0) / total * 100, 2),
            OcrTextPercent = Math.Round((job.PagesWithOcrTextCount ?? 0) / total * 100, 2),
            SearchableTextPagesPercent = Math.Round((job.PagesWithSearchableTextCount ?? 0) / total * 100, 2),
            MissingPagesCount = missingPagesCount
        };
    }
}
