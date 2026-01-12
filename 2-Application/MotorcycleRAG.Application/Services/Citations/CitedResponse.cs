using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services.Citations;

public sealed class CitedResponse
{
    public string Answer { get; init; } = string.Empty;

    public IReadOnlyList<SearchResult> Results { get; init; } = Array.Empty<SearchResult>();
}
