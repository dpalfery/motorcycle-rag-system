namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Blob path conventions for raw ingestion uploads.
/// </summary>
public static class IngestionBlobPaths
{
    private const string PdfSourceFileName = "source.pdf";

    public static string BuildRawUploadBlobName(string uploadId, string documentType) =>
        string.Equals(documentType, "manual-pdf", StringComparison.OrdinalIgnoreCase)
            ? $"{uploadId}/{PdfSourceFileName}"
            : $"{uploadId}.csv";
}
