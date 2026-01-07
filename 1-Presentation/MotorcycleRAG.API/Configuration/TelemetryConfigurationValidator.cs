using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.API.Configuration;

/// <summary>
/// Validator for Telemetry configuration
/// </summary>
internal class TelemetryConfigurationValidator : IValidateOptions<TelemetryOptions>
{
    ValidateOptionsResult IValidateOptions<TelemetryOptions>.Validate(string? name, TelemetryOptions options)
    {
        var failures = new List<string>();

        if (options.EnableTelemetry && string.IsNullOrWhiteSpace(options.ConnectionString))
            failures.Add("ApplicationInsights:ConnectionString is required when telemetry is enabled");

        if (string.IsNullOrWhiteSpace(options.ApplicationName))
            failures.Add("ApplicationInsights:ApplicationName is required");

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}