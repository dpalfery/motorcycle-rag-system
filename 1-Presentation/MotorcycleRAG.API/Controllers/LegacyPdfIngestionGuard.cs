using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.API.Controllers;

/// <summary>
/// Guards legacy direct-pipeline endpoints from handling PDF ingestion.
/// Supported PDF ingestion must run through the ingestion job flow and local Python processor.
/// </summary>
internal static class LegacyPdfIngestionGuard
{
    private const string Title = "Legacy PDF ingestion is no longer supported";
    private const string Detail =
        "PDF ingestion on legacy direct-pipeline endpoints is disabled. " +
        "Upload the file with POST /api/ingestion/jobs/upload and start processing with POST /api/ingestion/jobs " +
        "to use the local Python processor.";

    internal static bool IsLegacyPdfRequest(DataPipelineRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.FileType == FileType.PDF
            || HasPdfExtension(request.FileName)
            || HasPdfExtension(request.FilePath);
    }

    internal static bool HasLegacyPdfRequests(IEnumerable<DataPipelineRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        return requests.Any(IsLegacyPdfRequest);
    }

    internal static bool IsLegacyPdfFile(IFormFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return HasPdfExtension(file.FileName)
            || string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HasLegacyPdfFiles(IEnumerable<IFormFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        return files.Any(IsLegacyPdfFile);
    }

    internal static ProblemDetails CreateProblemDetails() =>
        new()
        {
            Title = Title,
            Detail = Detail,
            Status = StatusCodes.Status400BadRequest
        };

    private static bool HasPdfExtension(string? pathOrFileName) =>
        !string.IsNullOrWhiteSpace(pathOrFileName)
        && string.Equals(Path.GetExtension(pathOrFileName), ".pdf", StringComparison.OrdinalIgnoreCase);
}
