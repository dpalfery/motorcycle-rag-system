using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services.Citations;

public sealed class ClaimEvidence
{
    public IList<Citation> Citations { get; } = new List<Citation>();

    public IList<SearchResult> Results { get; } = new List<SearchResult>();
}
