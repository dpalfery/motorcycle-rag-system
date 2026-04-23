namespace MotorcycleRAG.Admin.Services.Dtos;

internal sealed class EmbeddingModelDiscoveryResponse {
    public string Provider { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public List<string> Models { get; set; } = [];
}