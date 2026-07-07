using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Features.Ingestion.Commands;

/// <summary>
/// Handles <see cref="StartFabricIngestionCommand"/> by delegating to <see cref="IIngestionJobService"/>.
/// </summary>
public sealed class StartFabricIngestionCommandHandler {
    private readonly IIngestionJobService _ingestionJobService;
    private readonly ILogger<StartFabricIngestionCommandHandler> _logger;

    public StartFabricIngestionCommandHandler(
        IIngestionJobService ingestionJobService,
        ILogger<StartFabricIngestionCommandHandler> logger) {
        _ingestionJobService = ingestionJobService ?? throw new ArgumentNullException(nameof(ingestionJobService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IngestionJobStatusResponse> HandleAsync(
        StartFabricIngestionCommand command,
        CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(command);

        _logger.LogInformation("Starting Fabric ingestion for user {UserId}", command.UserId);
        return await _ingestionJobService.StartJobAsync(command.Request, command.UserId, ct).ConfigureAwait(false);
    }
}
