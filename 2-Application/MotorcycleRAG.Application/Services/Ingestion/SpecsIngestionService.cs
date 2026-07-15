using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Orchestrates CSV specs ingestion: parses a CSV stream, builds <see cref="BikeModel"/> entities,
/// and upserts each row via <see cref="IBikeModelRepository.UpsertAsync"/>.
/// Required columns: Make, Model, Year. Rows missing any required value are skipped.
/// </summary>
public sealed class SpecsIngestionService {
    private readonly IBikeModelRepository _repository;
    private readonly ILogger<SpecsIngestionService> _logger;

    private static readonly char[] Separator = [','];

    public SpecsIngestionService(
        IBikeModelRepository repository,
        ILogger<SpecsIngestionService> logger) {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Parses the CSV stream and upserts each valid row as a <see cref="BikeModel"/>.
    /// </summary>
    /// <param name="uploadId">Upload batch reference.</param>
    /// <param name="userId">Subject/user ID performing the upload.</param>
    /// <param name="csvStream">CSV data stream with header row.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task IngestCsvAsync(
        string uploadId,
        string userId,
        Stream csvStream,
        CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(csvStream);

        using var reader = new StreamReader(csvStream, leaveOpen: true);

        // Read and parse header row
        var headerLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(headerLine)) {
            _logger.LogInformation("Upload {UploadId}: empty CSV stream, 0 rows ingested.", uploadId);
            return;
        }

        var headers = headerLine.Split(Separator, StringSplitOptions.TrimEntries);
        var columnMap = BuildColumnMap(headers);

        if (!columnMap.TryGetValue("make", out var makeIdx)
            || !columnMap.TryGetValue("model", out var modelIdx)
            || !columnMap.TryGetValue("year", out var yearIdx)) {
            _logger.LogDebug(
                "Upload {UploadId}: CSV missing one or more required columns (Make, Model, Year). Skipping all rows.",
                uploadId);
            return;
        }

        var rowCount = 0;
        var skippedCount = 0;

        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null) {

            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }

            var fields = line.Split(Separator, StringSplitOptions.TrimEntries);

            // Validate required fields are present and non-empty
            var make = GetField(fields, makeIdx);
            var model = GetField(fields, modelIdx);
            var yearRaw = GetField(fields, yearIdx);

            if (string.IsNullOrWhiteSpace(make)
                || string.IsNullOrWhiteSpace(model)
                || string.IsNullOrWhiteSpace(yearRaw)
                || !int.TryParse(yearRaw, out var year)) {
                skippedCount++;
                continue;
            }

            var bikeModel = BikeModel.Create(
                make,
                model,
                year,
                createdByUserId: userId,
                uploadRef: uploadId);

            await _repository.UpsertAsync(bikeModel, ct).ConfigureAwait(false);
            rowCount++;
        }

        if (skippedCount > 0) {
            _logger.LogDebug(
                "Upload {UploadId}: skipped {SkippedCount} rows with missing or invalid required fields.",
                uploadId,
                skippedCount);
        }

        _logger.LogInformation(
            "Upload {UploadId}: ingested {RowCount} rows.",
            uploadId,
            rowCount);
    }

    /// <summary>Builds a case-insensitive header-name-to-index map.</summary>
    private static Dictionary<string, int> BuildColumnMap(string[] headers) {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++) {
            var key = headers[i].Trim();
            if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key)) {
                map[key] = i;
            }
        }

        return map;
    }

    /// <summary>Safely retrieves a field value by index, returning null if out of bounds.</summary>
    private static string? GetField(string[] fields, int index) {
        return index < fields.Length ? fields[index] : null;
    }
}
