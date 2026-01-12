using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotorcycleRAG.API.Services;

/// <summary>
/// Custom health check response writer that formats health check results as structured JSON
/// </summary>
internal static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Writes a structured JSON health check response in the format expected by tests
    /// </summary>
    internal static Task WriteResponse(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";

        // Return 200 OK for all states to allow monitoring systems to process the full report.
        // The "status" field in the JSON indicates the actual health state.
        context.Response.StatusCode = StatusCodes.Status200OK;

        var response = new HealthCheckResponse
        {
            Status = report.Status.ToString(),
            TotalDuration = report.TotalDuration.ToString("G"), // TimeSpan format: HH:mm:ss.fffffff
            Checks = report.Entries.ToDictionary(
                kvp => kvp.Key,
                kvp => new HealthCheckEntry
                {
                    Status = kvp.Value.Status.ToString(),
                    Duration = kvp.Value.Duration.ToString("G"), // TimeSpan format: HH:mm:ss.fffffff
                    Description = kvp.Value.Description,
                    Data = kvp.Value.Data
                })
        };

        var json = JsonSerializer.Serialize(response, JsonOptions);
        return context.Response.WriteAsync(json);
    }
}
