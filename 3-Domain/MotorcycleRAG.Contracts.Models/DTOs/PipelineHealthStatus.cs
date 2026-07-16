using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace MotorcycleRAG.Contracts.Models.DTOs;

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

    public Dictionary<string, object> Data { get; } = new();
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

