using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Pipeline.Commands;

/// <summary>
/// CQRS command that carries the data needed to start a Fabric ingestion pipeline run.
/// </summary>
public sealed record StartFabricIngestionCommand(
    IngestionJobStartRequest Request,
    string UserId);
