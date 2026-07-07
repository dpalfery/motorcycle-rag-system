using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Persistence.DataProcessing;

/// <summary>
/// Predictable fallback for legacy PDF processing paths when Azure Document Intelligence is unavailable.
/// </summary>
public sealed class DisabledPdfProcessor : IDataProcessor<PDFDocument>
{
    private const string ErrorMessage =
        "Legacy PDF processing is disabled because Azure Document Intelligence is not configured. " +
        "Use POST /api/ingestion/jobs/upload and POST /api/ingestion/jobs to run PDF ingestion through the local Python processor.";

    private readonly ILogger<DisabledPdfProcessor> _logger;

    public DisabledPdfProcessor(ILogger<DisabledPdfProcessor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<ProcessedData> ProcessAsync(PDFDocument input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _logger.LogWarning("Legacy PDF processing was requested while disabled for file {FileName}", input.FileName);
        throw new InvalidOperationException(ErrorMessage);
    }
}
