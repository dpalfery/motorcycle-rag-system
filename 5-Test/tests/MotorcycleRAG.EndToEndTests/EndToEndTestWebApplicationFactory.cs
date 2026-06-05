using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.IntegrationTests;

namespace MotorcycleRAG.EndToEndTests;

public class EndToEndTestWebApplicationFactory : TestWebApplicationFactory {
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services => {
            // Neutralize Application Insights — the production TelemetryService wraps
            // every TelemetryClient.Track*() call and re-throws on failure. With no
            // connection string, even a disabled TelemetryConfiguration throws. Replace
            // both the config/client (for DI telemetric infrastructure) and the
            // application-level ITelemetryService with no-op stubs.
            services.RemoveAll<TelemetryConfiguration>();
            services.RemoveAll<TelemetryClient>();
            services.AddSingleton(_ => new TelemetryConfiguration { DisableTelemetry = true });
            services.AddSingleton(sp => new TelemetryClient(sp.GetRequiredService<TelemetryConfiguration>()));

            services.RemoveAll<ITelemetryService>();
            services.AddSingleton<ITelemetryService, NoOpTelemetryService>();

            services.RemoveAll<IAgentOrchestrator>();
            services.AddSingleton<IAgentOrchestrator, FakeEndToEndAgentOrchestrator>();
        });
    }
}

/// <summary>
/// No-op telemetry service for E2E tests. Absorbs all tracking calls silently
/// so tests never hit Application Insights infrastructure.
/// </summary>
internal sealed class NoOpTelemetryService : ITelemetryService {
    public void TrackEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null) { }
    public void TrackException(Exception exception, Dictionary<string, string>? properties = null) { }
    public void TrackMetric(string metricName, double value, Dictionary<string, string>? properties = null) { }
    public void TrackRequest(string name, DateTimeOffset startTime, TimeSpan duration, string responseCode, bool success) { }
    public void TrackQuery(string queryId, string query, TimeSpan duration, int resultsCount, decimal estimatedCost) { }
    public void TrackDegradedMode(string correlationId, IReadOnlyList<string> failedSources, IReadOnlyList<string> availableSources, TimeSpan duration, int resultsFound) { }
    public void TrackSourceFailure(string correlationId, string sourceName, string errorMessage, TimeSpan duration) { }
    public void TrackSearchExecution(string correlationId, string queryId, TimeSpan totalDuration, int totalResults, int successfulSources, int failedSources, bool degradedMode) { }
    public void TrackOnboardingTransition(string requestId, string stage, string state, string correlationId, TimeSpan duration) { }
    public void TrackAdminAction(string action, string targetId, string? actorUserId, bool success, TimeSpan duration) { }
    public void TrackDependencyDegradation(string dependencyName, string correlationId, string reason) { }
}