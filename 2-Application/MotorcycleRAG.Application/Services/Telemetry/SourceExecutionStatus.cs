using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services.Telemetry;

/// <summary>
/// Represents the execution status of a search source
/// </summary>
public class SourceExecutionStatus
{
    public SearchAgentType AgentType { get; set; }
    public bool Succeeded { get; set; }
    public int ResultsCount { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
}