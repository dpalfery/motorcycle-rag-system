using System.Text.Json;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs.Graph;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Reads graph entities (nodes and edges) extracted by the Python graph_extractor
/// from Azure Blob Storage and upserts them into the SQL Server Graph database
/// via <see cref="IGraphRepository"/>.
/// </summary>
public sealed class GraphEntityIngestionService : IGraphEntityIngestionService {
    private const string BlobContainer = "raw-uploads";

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    private readonly IBlobStorageService _blobStorage;
    private readonly IGraphRepository _graphRepository;
    private readonly ILogger<GraphEntityIngestionService> _logger;

    public GraphEntityIngestionService(
        IBlobStorageService blobStorage,
        IGraphRepository graphRepository,
        ILogger<GraphEntityIngestionService> logger) {
        ArgumentNullException.ThrowIfNull(blobStorage);
        ArgumentNullException.ThrowIfNull(graphRepository);
        ArgumentNullException.ThrowIfNull(logger);

        _blobStorage = blobStorage;
        _graphRepository = graphRepository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task IngestAsync(string uploadId, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);

        var blobPath = $"graph-entities/{uploadId}/entities.json";

        // Check if the blob exists before attempting download.
        bool exists;
        try {
            exists = await _blobStorage.ExistsAsync(BlobContainer, blobPath, cancellationToken)
                .ConfigureAwait(false);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to check existence of graph entities blob for upload {UploadId}", LogSanitizer.Sanitize(uploadId));
            return;
        }

        if (!exists) {
            _logger.LogWarning("No graph entities found for upload {UploadId}", LogSanitizer.Sanitize(uploadId));
            return;
        }

        // Download and deserialize the entities JSON.
        byte[] jsonBytes;
        try {
            await using var stream = await _blobStorage.DownloadAsync(BlobContainer, blobPath, cancellationToken)
                .ConfigureAwait(false);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            jsonBytes = memoryStream.ToArray();
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Failed to download graph entities blob for upload {UploadId}", LogSanitizer.Sanitize(uploadId));
            return;
        }

        if (jsonBytes.Length == 0) {
            _logger.LogWarning("Graph entities blob is empty for upload {UploadId}", LogSanitizer.Sanitize(uploadId));
            return;
        }

        // The Python graph_extractor writes: [{"nodes": [...], "edges": [...]}]
        List<GraphEntityDocument>? documents;
        try {
            documents = JsonSerializer.Deserialize<List<GraphEntityDocument>>(jsonBytes, JsonOptions);
        } catch (JsonException ex) {
            _logger.LogWarning(ex, "Failed to deserialize graph entities JSON for upload {UploadId}", LogSanitizer.Sanitize(uploadId));
            return;
        }

        if (documents is null || documents.Count == 0) {
            _logger.LogWarning("Graph entities JSON contained no documents for upload {UploadId}", LogSanitizer.Sanitize(uploadId));
            return;
        }

        // Take the first (and typically only) element.
        var document = documents[0];
        var nodes = MapNodes(document.Nodes);
        var edges = MapEdges(document.Edges, uploadId);

        if (nodes.Count > 0) {
            await _graphRepository.UpsertNodesAsync(nodes, cancellationToken).ConfigureAwait(false);
        }

        if (edges.Count > 0) {
            await _graphRepository.UpsertEdgesAsync(edges, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Ingested {NodeCount} nodes and {EdgeCount} edges for upload {UploadId}",
            nodes.Count,
            edges.Count,
            LogSanitizer.Sanitize(uploadId));
    }

    private static List<GraphNodeDto> MapNodes(List<GraphNodeJson>? nodeJsons) {
        if (nodeJsons is null || nodeJsons.Count == 0) {
            return [];
        }

        var nodes = new List<GraphNodeDto>(nodeJsons.Count);
        foreach (var nj in nodeJsons) {
            var id = Guid.TryParse(nj.Id, out var parsedId) ? parsedId : Guid.NewGuid();

            Guid? sourceDocId = null;
            if (!string.IsNullOrWhiteSpace(nj.SourceDocumentId)) {
                sourceDocId = Guid.TryParse(nj.SourceDocumentId, out var parsed) ? parsed : null;
            }

            nodes.Add(new GraphNodeDto {
                Id = id,
                Name = nj.Name,
                Type = nj.Type,
                Description = nj.Description,
                SourceDocumentId = sourceDocId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        return nodes;
    }

    private List<GraphEdgeDto> MapEdges(List<GraphEdgeJson>? edgeJsons, string uploadId) {
        if (edgeJsons is null || edgeJsons.Count == 0) {
            return [];
        }

        var edges = new List<GraphEdgeDto>(edgeJsons.Count);
        foreach (var ej in edgeJsons) {
            if (!Guid.TryParse(ej.FromNodeId, out var fromId)) {
                _logger.LogWarning(
                    "Skipping edge with invalid FromNodeId {FromNodeId} for upload {UploadId}",
                    LogSanitizer.Sanitize(ej.FromNodeId),
                    LogSanitizer.Sanitize(uploadId));
                continue;
            }

            if (!Guid.TryParse(ej.ToNodeId, out var toId)) {
                _logger.LogWarning(
                    "Skipping edge with invalid ToNodeId {ToNodeId} for upload {UploadId}",
                    LogSanitizer.Sanitize(ej.ToNodeId),
                    LogSanitizer.Sanitize(uploadId));
                continue;
            }

            edges.Add(new GraphEdgeDto {
                FromNodeId = fromId,
                ToNodeId = toId,
                RelationshipType = ej.RelationshipType,
                Weight = ej.Weight,
                Context = ej.Context,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        return edges;
    }

    #region JSON deserialization helpers (instantiated by System.Text.Json)

#pragma warning disable CA1812, S1144, S3459 // JSON deserialization classes: instantiated/set by JsonSerializer.Deserialize

    private sealed class GraphEntityDocument {
        public List<GraphNodeJson>? Nodes { get; set; }
        public List<GraphEdgeJson>? Edges { get; set; }
    }

    private sealed class GraphNodeJson {
        public string? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? SourceDocumentId { get; set; }
    }

    private sealed class GraphEdgeJson {
        public string? FromNodeId { get; set; }
        public string? ToNodeId { get; set; }
        public string RelationshipType { get; set; } = string.Empty;
        public double Weight { get; set; } = 1.0;
        public string? Context { get; set; }
    }

    #pragma warning restore CA1812, S1144, S3459

    #endregion
}
