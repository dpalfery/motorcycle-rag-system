using System.Text.Json.Serialization;

namespace MotorcycleRAG.API.Services;

/// <summary>
/// Health check response model.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Maintainability", "CA1515:Consider making public types internal",
    Justification = "Public DTO for health check endpoint response serialization")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "S4004:Utility classes should not have public constructors",
    Justification = "DTO for JSON serialization, requires instance type with public settable properties")]
public sealed class HealthCheckResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("totalDuration")]
    public string? TotalDuration { get; set; }

    [JsonPropertyName("checks")]
    public Dictionary<string, HealthCheckEntry>? Checks { get; set; }
}

