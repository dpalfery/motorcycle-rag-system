using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models;

/// <summary>
/// Usage tracking model for API calls
/// </summary>
public class Usage
{
    /// <summary>
    /// Unique identifier for usage record
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// User ID who made request
    /// </summary>
    [Required]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// API endpoint that was called
    /// </summary>
    [Required]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method used
    /// </summary>
    [Required]
    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>
    /// Query ID for tracking
    /// </summary>
    public string QueryId { get; set; } = string.Empty;

    /// <summary>
    /// Date and time when request was made
    /// </summary>
    public DateTime RequestTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Duration of request in milliseconds
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// Status code of response
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Whether request was successful
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// IP address of caller
    /// </summary>
    public string? CallerIp { get; set; }

    /// <summary>
    /// User agent string
    /// </summary>
    public string? UserAgent { get; set; }
}

/// <summary>
/// Daily usage summary model
/// </summary>
public class DailyUsageSummary
{
    /// <summary>
    /// User ID
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Date of usage
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Total count of requests
    /// </summary>
    public int RequestCount { get; set; }

    /// <summary>
    /// Average request duration in milliseconds
    /// </summary>
    public double AverageDurationMs { get; set; }

    /// <summary>
    /// Success rate percentage
    /// </summary>
    public double SuccessRate { get; set; }
}
