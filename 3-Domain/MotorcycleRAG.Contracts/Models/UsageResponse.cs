namespace MotorcycleRAG.Domain.Models;

/// <summary>
/// Usage response model
/// </summary>
public class UsageResponse
{
    public string UserId { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public int DailyUsageCount { get; set; }
    public int DailyRequestLimit { get; set; }
    public int RemainingDailyRequests { get; set; }
    public UsageRecord[] UsageRecords { get; set; } = Array.Empty<UsageRecord>();
}

/// <summary>
/// Usage record model
/// </summary>
public class UsageRecord
{
    public long Id { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public string QueryId { get; set; } = string.Empty;
    public DateTime RequestTime { get; set; }
    public long DurationMs { get; set; }
    public int StatusCode { get; set; }
    public bool IsSuccess { get; set; }
}
