using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.HttpOverrides;
using MotorcycleRag.WebUI.BFF.Configuration;
using MotorcycleRag.WebUI.BFF.Configuration.Services;
using MotorcycleRag.WebUI.BFF.Extensions;
using MotorcycleRag.WebUI.BFF.HealthChecks;
using MotorcycleRag.WebUI.BFF.Middleware;

var builder = WebApplication.CreateBuilder(args);

// 1. Bootstrap Telemetry (Pre-DI for early failure capture)
var bootstrapAiConnectionString = builder.Configuration.GetConnectionString("ApplicationInsights")
    ?? builder.Configuration["ApplicationInsights:ConnectionString"];

TelemetryClient? bootstrapTelemetry = null;
if (!string.IsNullOrEmpty(bootstrapAiConnectionString))
{
    var bootstrapTelemetryConfig = new TelemetryConfiguration { ConnectionString = bootstrapAiConnectionString };
    bootstrapTelemetry = new TelemetryClient(bootstrapTelemetryConfig);
}

// 2. Core Configuration & Infrastructure
builder.AddBffAzureAppConfiguration();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllers();

// Disable built-in HostFilter in favor of custom HostHeaderValidationMiddleware
builder.Services.PostConfigure<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>(options =>
{
    options.AllowedHosts = ["*"];
});

// 3. Service Configuration Extensions
builder.Services.AddBffCors(builder.Configuration);
builder.Services.AddBffTelemetry(builder.Configuration);

builder.Services.AddHealthChecks()
    .AddCheck<DataProtectionHealthCheck>("data_protection_blob");

builder.Services.AddDataProtectionMonitoring();
builder.Services.AddBffReverseProxy(builder.Configuration);
builder.Services.AddBffAuthentication(builder.Configuration);
builder.Services.AddBffDataProtection(builder.Configuration, builder.Environment);

// 4. Build and Pipeline
var app = builder.Build();

// Monitoring & Logging
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var dpBlobUri = builder.Configuration["DataProtection:BlobUri"];
if (!string.IsNullOrEmpty(dpBlobUri))
{
    logger.LogInformation("Data Protection keys persisted to Azure Blob Storage: {BlobUri}", dpBlobUri);
    app.Services.GetService<DataProtectionMonitoringService>()?.TrackKeysInitialized(dpBlobUri);
}

app.UseMotorcycleRagBffMiddleware();

try
{
    await app.RunAsync().ConfigureAwait(false);
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"FATAL STARTUP ERROR: {ex}").ConfigureAwait(false);
    if (bootstrapTelemetry is not null)
    {
        bootstrapTelemetry.TrackException(ex, new Dictionary<string, string>
        {
            ["source"] = "bootstrap",
            ["severity"] = "fatal"
        });
        bootstrapTelemetry.Flush();
        await Task.Delay(2000).ConfigureAwait(false);
    }
    throw;
}
