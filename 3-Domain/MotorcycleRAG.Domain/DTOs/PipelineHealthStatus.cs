using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MotorcycleRAG.Domain.DTOs;

/// <summary>
/// Pipeline health status
/// </summary>
public class PipelineHealthStatus
{
    public OverallHealthStatus Status { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    public List<HealthCheck> HealthChecks { get; set; } = new();

    public Dictionary<string, object> Metrics { get; set; } = new();

    public List<string> Alerts { get; set; } = new();
}

/// <summary>
/// Individual health check result
/// </summary>
public class HealthCheck
{
    public string Name { get; set; } = string.Empty;

    public HealthCheckStatus Status { get; set; }

    public string Description { get; set; } = string.Empty;

    public TimeSpan Duration { get; set; }

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;

    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>
/// Health check status enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HealthCheckStatus
{
    Healthy,
    Degraded,
    Unhealthy
}

/// <summary>
/// Overall health status
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OverallHealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}
