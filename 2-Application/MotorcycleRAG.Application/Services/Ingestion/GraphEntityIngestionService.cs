using System.Text.Json;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs.Graph;
using MotorcycleRAG.Contracts.Repositories;

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
    private readonly IManualDocumentRepository _manualDocumentRepository;
    private readonly ILogger<GraphEntityIngestionService> _logger;

    /// <summary>
    /// Initializes the service with all dependencies, including the document repository used to
    /// resolve <see cref="ManualDocument.SourceContentHash"/> for the D4 vector&lt;-&gt;graph anchor.
    /// </summary>
    public GraphEntityIngestionService(
        IBlobStorageService blobStorage,
        IGraphRepository graphRepository,
        IManualDocumentRepository manualDocumentRepository,
        ILogger<GraphEntityIngestionService> logger) {
        ArgumentNullException.ThrowIfNull(blobStorage);
        ArgumentNullException.ThrowIfNull(graphRepository);
        ArgumentNullException.ThrowIfNull(manualDocumentRepository);
        ArgumentNullException.ThrowIfNull(logger);

        _blobStorage = blobStorage;
        _graphRepository = graphRepository;
        _manualDocumentRepository = manualDocumentRepository;
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
            _logger.LogWarning(ex, "Failed to check existence of graph entities blob for upload {UploadId}", uploadId);
            return;
        }

        if (!exists) {
            _logger.LogWarning("No graph entities found for upload {UploadId}", uploadId);
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
            _logger.LogWarning(ex, "Failed to download graph entities blob for upload {UploadId}", uploadId);
            return;
        }

        if (jsonBytes.Length == 0) {
            _logger.LogWarning("Graph entities blob is empty for upload {UploadId}", uploadId);
            return;
        }

        // The Python graph_extractor writes: [{"nodes": [...], "edges": [...]}]
        List<GraphEntityDocument>? documents;
        try {
            documents = JsonSerializer.Deserialize<List<GraphEntityDocument>>(jsonBytes, JsonOptions);
        } catch (JsonException ex) {
            _logger.LogWarning(ex, "Failed to deserialize graph entities JSON for upload {UploadId}", uploadId);
            return;
        }

        if (documents is null || documents.Count == 0) {
            _logger.LogWarning("Graph entities JSON contained no documents for upload {UploadId}", uploadId);
            return;
        }

        // Take the first (and typically only) element.
        var document = documents[0];
        var nodes = await MapNodesAsync(document.Nodes, uploadId, cancellationToken).ConfigureAwait(false);

        // Build an Id -> Type lookup over the surviving (non-quarantined) nodes so MapEdges can
        // enforce the SOURCED_FROM/PART_OF endpoint-type rules from decision D2.
        var nodeTypeById = new Dictionary<Guid, string>();
        foreach (var node in nodes) {
            nodeTypeById[node.Id] = node.Type;
        }

        var edges = MapEdges(document.Edges, nodeTypeById, uploadId);

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
            uploadId);
    }

    private async Task<List<GraphNodeDto>> MapNodesAsync(
        List<GraphNodeJson>? nodeJsons,
        string uploadId,
        CancellationToken cancellationToken) {
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

            // D2 narrow field-shape validation: a Chunk-type node MUST carry a non-empty chunkId
            // (its vector<->graph anchor). A Chunk node missing chunkId is defective and is
            // quarantined (logged + skipped), never thrown on, per the no-throw ingestion convention.
            if (IsChunkType(nj.Type) && string.IsNullOrWhiteSpace(nj.ChunkId)) {
                _logger.LogWarning(
                    "Quarantined Chunk-type graph node {NodeId} without a chunkId for upload {UploadId}",
                    id,
                    uploadId);
                continue;
            }

            nodes.Add(new GraphNodeDto {
                Id = id,
                Name = nj.Name,
                Type = nj.Type,
                Description = nj.Description,
                SourceDocumentId = sourceDocId,
                // The singular ChunkId anchor belongs only to Chunk-type nodes; non-Chunk nodes
                // carry sourceChunkIds at the extraction layer (emitted as SOURCED_FROM edges), not here.
                ChunkId = IsChunkType(nj.Type) ? nj.ChunkId : null,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        // D4: stamp ManualDocument.SourceContentHash on every node whose owning document resolves.
        await StampSourceContentHashAsync(nodes, uploadId, cancellationToken).ConfigureAwait(false);

        return nodes;
    }

    /// <summary>
    /// Resolves <see cref="ManualDocument.SourceContentHash"/> once per distinct source document and stamps
    /// it on every node attributed to that document. A missing document leaves
    /// <see cref="GraphNodeDto.SourceContentHash"/> as <c>null</c> — never <see cref="string.Empty"/> — so
    /// downstream null-omission guards stay intact. Failures to resolve a single document are logged and
    /// skipped; they never abort the whole ingestion.
    /// </summary>
    private async Task StampSourceContentHashAsync(
        List<GraphNodeDto> nodes,
        string uploadId,
        CancellationToken cancellationToken) {
        var documentIds = nodes
            .Where(n => n.SourceDocumentId.HasValue)
            .Select(n => n.SourceDocumentId!.Value)
            .Distinct()
            .ToList();

        if (documentIds.Count == 0) {
            return;
        }

        var hashByDocument = new Dictionary<Guid, string?>();
        foreach (var documentId in documentIds) {
            try {
                var document = await _manualDocumentRepository
                    .GetDocumentByIdAsync(documentId, cancellationToken)
                    .ConfigureAwait(false);
                hashByDocument[documentId] = document?.SourceContentHash;
            } catch (Exception ex) {
                _logger.LogWarning(ex,
                    "Failed to resolve ManualDocument {DocumentId} for SourceContentHash stamping on upload {UploadId}",
                    documentId,
                    uploadId);
                // Leave this document out of the map; affected nodes keep their default (null) hash.
            }
        }

        foreach (var node in nodes) {
            if (node.SourceDocumentId.HasValue
                && hashByDocument.TryGetValue(node.SourceDocumentId.Value, out var hash)) {
                node.SourceContentHash = hash;
            }
        }
    }

    private List<GraphEdgeDto> MapEdges(
        List<GraphEdgeJson>? edgeJsons,
        IReadOnlyDictionary<Guid, string> nodeTypeById,
        string uploadId) {
        if (edgeJsons is null || edgeJsons.Count == 0) {
            return [];
        }

        var edges = new List<GraphEdgeDto>(edgeJsons.Count);
        foreach (var ej in edgeJsons) {
            if (!Guid.TryParse(ej.FromNodeId, out var fromId)) {
                _logger.LogWarning(
                    "Skipping edge with invalid FromNodeId {FromNodeId} for upload {UploadId}",
                    ej.FromNodeId,
                    uploadId);
                continue;
            }

            if (!Guid.TryParse(ej.ToNodeId, out var toId)) {
                _logger.LogWarning(
                    "Skipping edge with invalid ToNodeId {ToNodeId} for upload {UploadId}",
                    ej.ToNodeId,
                    uploadId);
                continue;
            }

            if (IsEdgeEndpointViolation(ej.RelationshipType, fromId, toId, nodeTypeById, uploadId)) {
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

    /// <summary>
    /// Applies the D2 narrow edge-shape rules: a SOURCED_FROM edge must target a Chunk-type node, and a
    /// PART_OF edge must connect a Chunk node to a Document node. Enforcement is conservative — an edge is
    /// quarantined only when an endpoint is resolvable in <paramref name="nodeTypeById"/> and provably violates
    /// the rule. Legacy edges whose endpoints predate the Chunk/Document ontology (e.g. Component→Component
    /// PART_OF) pass through unchanged so existing extractions are not regressed.
    /// </summary>
    private bool IsEdgeEndpointViolation(
        string relationshipType,
        Guid fromId,
        Guid toId,
        IReadOnlyDictionary<Guid, string> nodeTypeById,
        string uploadId) {
        if (string.Equals(relationshipType, "SOURCED_FROM", StringComparison.OrdinalIgnoreCase)) {
            if (nodeTypeById.TryGetValue(toId, out var toType) && !IsChunkType(toType)) {
                _logger.LogWarning(
                    "Quarantined SOURCED_FROM edge from {FromNodeId} to non-Chunk node {ToNodeId} ({ToType}) for upload {UploadId}",
                    fromId,
                    toId,
                    toType,
                    uploadId);
                return true;
            }
        } else if (string.Equals(relationshipType, "PART_OF", StringComparison.OrdinalIgnoreCase)) {
            // Only enforce when the source endpoint is a Chunk node — the new anchor pattern. A Chunk node
            // claiming PART_OF must target a Document node.
            if (nodeTypeById.TryGetValue(fromId, out var fromType) && IsChunkType(fromType)) {
                if (!nodeTypeById.TryGetValue(toId, out var toType) || !IsDocumentType(toType)) {
                    _logger.LogWarning(
                        "Quarantined PART_OF edge from Chunk node {FromNodeId} to non-Document endpoint {ToNodeId} for upload {UploadId}",
                        fromId,
                        toId,
                        uploadId);
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsChunkType(string? type) =>
        string.Equals(type, "Chunk", StringComparison.OrdinalIgnoreCase);

    private static bool IsDocumentType(string? type) =>
        string.Equals(type, "Document", StringComparison.OrdinalIgnoreCase);

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
        /// <summary>
        /// The canonical chunk anchor (format <c>{uploadId}-pdf-{i}</c>) emitted by the Python
        /// graph_extractor for Chunk-type nodes. Required for Type=="Chunk"; ignored for other types.
        /// </summary>
        public string? ChunkId { get; set; }
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
