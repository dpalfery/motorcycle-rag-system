using System.Text.Json;
using MotorcycleRAG.Application.Services.Ingestion;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Application.Features.Ingestion.Mappers;

/// <summary>
/// Maps <see cref="IngestionJob"/> domain entities to <see cref="IngestionJobStatusResponse"/> DTOs.
/// Supports FR-010 (status) and FR-010a (coverage reporting).
/// </summary>
public static class IngestionJobStatusMapper
{
    private const string NeedsManualMetadataStage = "needs-manual-metadata";

    private const int MetadataRequiredFieldCount = 4;

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

        var metadata = TryParseJobMetadata(job.MetadataJson);

        return new IngestionJobStatusResponse
        {
            Id = job.Id,
            JobId = job.IngestionJobId,
            Status = job.Status.ToString(),
            CreatedAtUtc = job.CreatedAtUtc,
            StartedAtUtc = job.StartedAtUtc,
            CompletedAtUtc = job.CompletedAtUtc,
            InputType = job.InputType.ToString(),
            InputRef = job.InputRef,
            SourceFileName = job.SourceFileName,
            ComputeProvider = job.ComputeProvider,
            ManualDocumentId = job.ManualDocumentId,
            TotalPages = job.TotalPages,
            PagesCapturedViewableCount = job.PagesCapturedViewableCount,
            PagesWithSearchableTextCount = job.PagesWithSearchableTextCount,
            PagesWithOcrTextCount = job.PagesWithOcrTextCount,
            PagesWithNativeTextCount = job.PagesWithNativeTextCount,
            MissingPages = missingPages.AsReadOnly(),
            Coverage = CoverageCalculator.Calculate(job),
            FailureReason = job.ErrorsJson ?? job.FailureReason,
            FailureDetail = job.ErrorsJson,
            DocIngestionRunId = job.DocIngestionRunId,
            RequiresManualMetadata = job.Status == IngestionJobStatus.AwaitingMetadata
                || string.Equals(job.CurrentStage, NeedsManualMetadataStage, StringComparison.OrdinalIgnoreCase),
            Make = metadata?.Make,
            Model = metadata?.Model,
            Year = metadata?.Year,
            Category = metadata?.Category,
            Tags = metadata?.Tags,
            FillRate = metadata?.FillRate,
            IsComplete = metadata?.IsComplete
        };
    }

    /// <summary>
    /// Parses <c>job.MetadataJson</c> into the parsed-metadata fields exposed on the response
    /// DTO. Returns null when the JSON is absent, empty, malformed, or not a JSON object — the
    /// GET path must never 500 because of corrupt stored data. Mirrors the parsing conventions
    /// of <c>IngestionJobService</c> (snake_case keys, year accepts number or numeric string),
    /// with a PascalCase fallback so manually-authored JSON blobs are also accepted.
    /// </summary>
    private static ParsedJobMetadata? TryParseJobMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return ExtractJobMetadata(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ParsedJobMetadata ExtractJobMetadata(JsonElement root)
    {
        var make = TryGetMetadataString(root, "make", "Make");
        var model = TryGetMetadataString(root, "model", "Model");
        var year = TryGetMetadataInt(root, "year", "Year");
        var category = TryGetMetadataString(root, "category", "Category");
        var tags = TryGetMetadataStringList(root, "tags", "Tags");

        var filled = 0;
        if (!string.IsNullOrWhiteSpace(make)) filled++;
        if (!string.IsNullOrWhiteSpace(model)) filled++;
        if (year.HasValue && year.Value > 0) filled++;
        if (!string.IsNullOrWhiteSpace(category)) filled++;

        var fillRate = (double)filled / MetadataRequiredFieldCount;

        return new ParsedJobMetadata(
            NormalizeMetadataString(make),
            NormalizeMetadataString(model),
            year,
            NormalizeMetadataString(category),
            tags.Count > 0 ? tags : null,
            fillRate,
            filled == MetadataRequiredFieldCount);
    }

    private static string? TryGetMetadataString(JsonElement root, string snakeCase, string pascalCase)
    {
        if (TryGetStringProperty(root, snakeCase, out var value))
        {
            return value;
        }

        TryGetStringProperty(root, pascalCase, out value);
        return value;
    }

    private static bool TryGetStringProperty(JsonElement root, string propertyName, out string? value)
    {
        if (root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static int? TryGetMetadataInt(JsonElement root, string snakeCase, string pascalCase)
    {
        if (TryGetIntProperty(root, snakeCase, out var numValue))
        {
            return numValue;
        }

        if (TryGetIntProperty(root, pascalCase, out numValue))
        {
            return numValue;
        }

        return null;
    }

    private static bool TryGetIntProperty(JsonElement root, string propertyName, out int value)
    {
        if (!root.TryGetProperty(propertyName, out var prop))
        {
            value = 0;
            return false;
        }

        // Accept year as a JSON number first, then fall back to a numeric string (some
        // LLMs emit "2023" instead of 2023).
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value))
        {
            return true;
        }

        if (prop.ValueKind == JsonValueKind.String)
        {
            var str = prop.GetString();
            if (int.TryParse(str, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static List<string> TryGetMetadataStringList(JsonElement root, string snakeCase, string pascalCase)
    {
        var result = new List<string>();

        // Try snake_case first, then fall back to PascalCase.
        CollectStringArray(root, snakeCase, result);
        if (result.Count == 0)
        {
            CollectStringArray(root, pascalCase, result);
        }

        return result;
    }

    private static void CollectStringArray(JsonElement root, string propertyName, List<string> destination)
    {
        if (!root.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    destination.Add(value.Trim());
                }
            }
        }
    }

    private static string? NormalizeMetadataString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ParsedJobMetadata(
        string? Make,
        string? Model,
        int? Year,
        string? Category,
        List<string>? Tags,
        double FillRate,
        bool IsComplete);
}
