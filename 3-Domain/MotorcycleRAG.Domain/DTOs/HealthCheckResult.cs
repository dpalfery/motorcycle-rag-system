using System;
using System.Collections.Generic;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Health check result
/// </summary>
public class HealthCheckResult
{
    public bool IsHealthy { get; set; }
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, object> Details { get; set; } = new();
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}
