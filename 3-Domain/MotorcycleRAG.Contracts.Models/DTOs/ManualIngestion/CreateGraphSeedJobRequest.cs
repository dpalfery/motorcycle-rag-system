namespace MotorcycleRAG.Contracts.Models.DTOs.ManualIngestion;

public record CreateGraphSeedJobRequest(
    string FileName,
    string? ContentHash);
