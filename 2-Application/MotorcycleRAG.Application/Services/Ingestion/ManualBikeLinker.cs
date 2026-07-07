using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Domain.Entities;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// After a PDF manual is ingested, links the <see cref="MotorcycleManual"/> entity
/// to the matching <see cref="BikeModel"/> via SQL Graph by creating a <see cref="GraphEdge"/>.
/// </summary>
public sealed class ManualBikeLinker
{
    private readonly IBikeModelRepository _bikeModelRepository;
    private readonly IGraphRepository _graphRepository;
    private readonly ILogger<ManualBikeLinker> _logger;

    public ManualBikeLinker(
        IBikeModelRepository bikeModelRepository,
        IGraphRepository graphRepository,
        ILogger<ManualBikeLinker> logger)
    {
        _bikeModelRepository = bikeModelRepository ?? throw new ArgumentNullException(nameof(bikeModelRepository));
        _graphRepository = graphRepository ?? throw new ArgumentNullException(nameof(graphRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Links a manual to its canonical bike model via a graph edge.
    /// If the bike model is not found, logs a warning and returns without throwing.
    /// </summary>
    /// <param name="manualId">The ID of the ingested manual.</param>
    /// <param name="make">Motorcycle manufacturer.</param>
    /// <param name="model">Motorcycle model designation.</param>
    /// <param name="year">Production year.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task LinkAsync(Guid manualId, string make, string model, int year, CancellationToken ct = default)
    {
        var bikeModel = await _bikeModelRepository.FindCanonicalAsync(make, model, year, ct).ConfigureAwait(false);

        if (bikeModel is null)
        {
            _logger.LogWarning("Cannot link manual to bike model — canonical bike model not found. ManualId={ManualId}", manualId);
            return;
        }

        var edge = new GraphEdge
        {
            FromNodeId = manualId,
            ToNodeId = bikeModel.Id,
            RelationshipType = "manual-for-bike"
        };

        await _graphRepository.UpsertEdgeAsync(edge, ct).ConfigureAwait(false);

        _logger.LogInformation("Manual linked to bike model. ManualId={ManualId}", manualId);
    }
}
