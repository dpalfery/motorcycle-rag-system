using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using NCrontab;

namespace MotorcycleRAG.Application.Services.Ingestion;

/// <summary>
/// Background service for scheduled processing of data pipelines
/// </summary>
#pragma warning disable S103 // Lines should not be too long
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "S1200:Split this class into smaller and more specialized ones",
    Justification = "Scheduled processing service orchestrates multiple dependencies by design.")]
#pragma warning restore S103 // Lines should not be too long
public class ScheduledPipelineService : BackgroundService, IScheduledPipelineService {
    private const string LegacyPdfProcessingDisabledMessage =
        "Legacy scheduled PDF processing is disabled when Azure Document Intelligence is not configured. " +
        "Use the ingestion jobs/local Python processor flow instead.";

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<ScheduledPipelineService> _logger;
    private readonly ScheduledProcessingConfiguration _config;
    private readonly SemaphoreSlim _executionSemaphore;
    private readonly bool _isDocumentIntelligenceEnabled;

    private ProcessingScheduleConfig _scheduleConfig;
    private CrontabSchedule? _schedule;
    private DateTime? _nextExecutionTime;
    private readonly ScheduledProcessingStats _stats;
    private CancellationTokenSource? _cancellationTokenSource;

    public ScheduledPipelineService(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<ScheduledProcessingConfiguration> config,
        IOptions<AzureFoundryOptions> azureFoundryOptions,
        ILogger<ScheduledPipelineService> logger) {
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(azureFoundryOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _serviceScopeFactory = serviceScopeFactory;
        _config = config.Value;

        _isDocumentIntelligenceEnabled = Uri.TryCreate(
            azureFoundryOptions.Value.DocumentIntelligenceEndpoint,
            UriKind.Absolute,
            out _);
        _logger = logger;

        _executionSemaphore = new SemaphoreSlim(1, 1);
        _stats = new ScheduledProcessingStats();

        // Initialize with default schedule
        _scheduleConfig = new ProcessingScheduleConfig {
            CronExpression = _config.DefaultCronExpression,
            IsEnabled = _config.IsEnabledByDefault,
            ProcessingWindow = _config.DefaultProcessingWindow,
            MaxConcurrentJobs = _config.DefaultMaxConcurrentJobs
        };

        UpdateScheduleInternal();
    }

    public override async Task StartAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("Starting scheduled pipeline service with cron expression: {CronExpression}", _scheduleConfig.CronExpression);

        if (_scheduleConfig.IsEnabled) {
            _cancellationTokenSource = new CancellationTokenSource();
            await base.StartAsync(cancellationToken);
            _logger.LogInformation("Scheduled pipeline service started. Next execution: {NextExecution}", _nextExecutionTime);
        }
        else {
            _logger.LogInformation("Scheduled pipeline service is disabled");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("Stopping scheduled pipeline service");

        if (_cancellationTokenSource != null) {
            await _cancellationTokenSource.CancelAsync();
        }
        await base.StopAsync(cancellationToken);

        _logger.LogInformation("Scheduled pipeline service stopped");
    }
    public async Task<PipelineExecutionResult> ExecuteImmediateRunAsync(CancellationToken cancellationToken = default) {
        await _executionSemaphore.WaitAsync(cancellationToken);

        try {
            _logger.LogInformation("Executing immediate scheduled pipeline run");

            using var scope = _serviceScopeFactory.CreateScope();
            return await ExecuteScheduledProcessingAsync(scope.ServiceProvider, cancellationToken);
        }
        finally {
            _executionSemaphore.Release();
        }
    }

    public async Task<DateTime?> GetNextExecutionTimeAsync() {
        return await Task.FromResult(_nextExecutionTime);
    }

    public async Task<ScheduledProcessingStats> GetProcessingStatsAsync() {
        return await Task.FromResult(_stats);
    }

    public async Task UpdateScheduleAsync(ProcessingScheduleConfig config) {
        ArgumentNullException.ThrowIfNull(config);
        _scheduleConfig = config;
        UpdateScheduleInternal();

        _logger.LogInformation("Schedule updated. New cron expression: {CronExpression}, Enabled: {Enabled}",
            config.CronExpression, config.IsEnabled);

        await Task.CompletedTask;
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!_scheduleConfig.IsEnabled) {
            _logger.LogInformation("Scheduled processing is disabled, service will not execute");
            return;
        }

        while (!stoppingToken.IsCancellationRequested) {
            try {
                var now = DateTime.UtcNow;
                var nextRun = GetNextScheduledTime(now);

                if (nextRun.HasValue) {
                    _nextExecutionTime = nextRun.Value;
                    var delay = nextRun.Value - now;

                    if (delay > TimeSpan.Zero) {
                        _logger.LogDebug("Next scheduled execution in {Delay} at {NextRun}", delay, nextRun.Value);
                        await Task.Delay(delay, stoppingToken);
                    }

                    if (!stoppingToken.IsCancellationRequested) {
                        await _executionSemaphore.WaitAsync(stoppingToken);

                        try {
                            using var scope = _serviceScopeFactory.CreateScope();
                            await ExecuteScheduledProcessingAsync(scope.ServiceProvider, stoppingToken);
                        }
                        finally {
                            _executionSemaphore.Release();
                        }
                    }
                }
                else {
                    // If no next run time, wait a reasonable interval before checking again
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) {
                // Expected when cancellation is requested
                return;
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error in scheduled pipeline service execution loop");

                // Wait before retrying to avoid tight error loops
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task<PipelineExecutionResult> ExecuteScheduledProcessingAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken) {
        var startTime = DateTime.UtcNow;
        var result = new PipelineExecutionResult {
            ExecutionId = Guid.NewGuid().ToString(),
            StartTime = startTime,
            Status = PipelineStatus.Processing
        };

        try {
            _logger.LogInformation("Starting scheduled pipeline processing at {StartTime}", startTime);

            // Update stats
            _stats.TotalScheduledRuns++;
            _stats.LastExecutionTime = startTime;
            _stats.LastExecutionStatus = PipelineStatus.Processing;

            // Find files to process
            var discoveryResult = await DiscoverFilesToProcessAsync();
            _logger.LogInformation("Found {FileCount} files to process", discoveryResult.Requests.Count);

            if (discoveryResult.SkippedLegacyPdfFiles.Count > 0) {
                var skipMessage = BuildLegacyPdfSkipMessage(discoveryResult.SkippedLegacyPdfFiles.Count);
                result.Warnings.Add(skipMessage);

                foreach (var skippedFile in discoveryResult.SkippedLegacyPdfFiles) {
                    result.Warnings.Add($"Skipped legacy scheduled PDF file: {skippedFile}");
                }

                _logger.LogWarning(
                    "Skipped {SkippedPdfCount} legacy scheduled PDF file(s) because Azure Document Intelligence is not configured",
                    discoveryResult.SkippedLegacyPdfFiles.Count);
            }

            if (discoveryResult.Requests.Count == 0) {
                result.Status = PipelineStatus.Completed;
                result.Message = discoveryResult.SkippedLegacyPdfFiles.Count > 0
                    ? BuildLegacyPdfSkipMessage(discoveryResult.SkippedLegacyPdfFiles.Count)
                    : "No files found to process";
                result.EndTime = DateTime.UtcNow;

                _stats.SuccessfulRuns++;
                _stats.LastExecutionStatus = PipelineStatus.Completed;
                _stats.FilesProcessedInLastRun = 0;
                _stats.AverageProcessingTime = TimeSpan.FromMilliseconds(
                    (_stats.AverageProcessingTime.TotalMilliseconds * (_stats.TotalScheduledRuns - 1) + result.Duration.TotalMilliseconds) / _stats.TotalScheduledRuns);

                return result;
            }

            // Process files in batch
            var orchestrator = serviceProvider.GetRequiredService<IDataPipelineOrchestrator>();
            var batchResult = await orchestrator.ProcessBatchAsync(discoveryResult.Requests, cancellationToken);

            // Update result based on batch processing
            result.Status = batchResult.HasErrors ? PipelineStatus.PartiallyCompleted : PipelineStatus.Completed;
            result.EndTime = batchResult.EndTime;
            result.Message = $"Processed {batchResult.ProcessedSuccessfully} files successfully, {batchResult.Failed} failed";

            if (discoveryResult.SkippedLegacyPdfFiles.Count > 0) {
                result.Status = PipelineStatus.PartiallyCompleted;
                result.Message = $"{result.Message}. {BuildLegacyPdfSkipMessage(discoveryResult.SkippedLegacyPdfFiles.Count)}";
            }

            if (batchResult.HasErrors) {
                foreach (var e in batchResult.Results.SelectMany(r => r.Errors)) {
                    result.Errors.Add(e);
                }
            }

            // Update statistics
            _stats.LastExecutionStatus = result.Status;
            _stats.FilesProcessedInLastRun = batchResult.TotalFiles;

            if (result.Status == PipelineStatus.Completed) {
                _stats.SuccessfulRuns++;
            }
            else {
                _stats.FailedRuns++;
                _stats.LastErrorMessage = result.Message;
            }

            var duration = result.EndTime!.Value - startTime;
            _stats.AverageProcessingTime = TimeSpan.FromMilliseconds(
                (_stats.AverageProcessingTime.TotalMilliseconds * (_stats.TotalScheduledRuns - 1) + duration.TotalMilliseconds) / _stats.TotalScheduledRuns);

            _logger.LogInformation("Scheduled pipeline processing completed. Status: {Status}, Duration: {Duration}",
                result.Status, duration);

            return result;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Scheduled pipeline processing failed");

            result.Status = PipelineStatus.Failed;
            result.EndTime = DateTime.UtcNow;
            result.Errors.Add(ex.Message);
            result.Message = $"Scheduled processing failed: {ex.Message}";

            _stats.FailedRuns++;
            _stats.LastExecutionStatus = PipelineStatus.Failed;
            _stats.LastErrorMessage = ex.Message;

            return result;
        }
    }

    private async Task<FileDiscoveryResult> DiscoverFilesToProcessAsync() {
        var discoveryResult = new FileDiscoveryResult();

        try {
            var processingDirectory = Path.Combine(_config.BaseDirectory, _scheduleConfig.ProcessingDirectory);

            if (!Directory.Exists(processingDirectory)) {
                Directory.CreateDirectory(processingDirectory);
                return discoveryResult;
            }

            var files = Directory.GetFiles(processingDirectory, "*.*", SearchOption.TopDirectoryOnly);

            foreach (var filePath in files) {
                var fileName = Path.GetFileName(filePath);
                var extension = Path.GetExtension(fileName).ToUpperInvariant();

                if (string.Equals(extension, ".PDF", StringComparison.OrdinalIgnoreCase) && !_isDocumentIntelligenceEnabled) {
                    discoveryResult.SkippedLegacyPdfFiles.Add(fileName);
                    continue;
                }

                var fileType = extension switch {
                    ".CSV" => FileType.CSV,
                    ".PDF" => FileType.PDF,
                    _ => FileType.Unknown
                };

                if (fileType != FileType.Unknown) {
                    discoveryResult.Requests.Add(new DataPipelineRequest {
                        FileName = fileName,
                        FilePath = filePath,
                        FileType = fileType,
                        CreatedBy = "ScheduledService",
                        Options = new PipelineOptions {
                            IndexImmediately = true,
                            ProcessImages = true,
                            GenerateEmbeddings = true
                        }
                    });
                }
            }

            _logger.LogDebug("Discovered {FileCount} files for processing in {Directory}", discoveryResult.Requests.Count, processingDirectory);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error discovering files to process");
        }

        return discoveryResult;
    }

    private static string BuildLegacyPdfSkipMessage(int skippedPdfCount) =>
        $"Skipped {skippedPdfCount} legacy scheduled PDF file(s). {LegacyPdfProcessingDisabledMessage}";

    private DateTime? GetNextScheduledTime(DateTime fromTime) {
        try {
            if (_schedule == null || !_scheduleConfig.IsEnabled)
                return null;

            return _schedule.GetNextOccurrence(fromTime);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Error calculating next scheduled time with cron expression: {CronExpression}", _scheduleConfig.CronExpression);
            return null;
        }
    }

    private static CrontabSchedule ParseCronExpression(string cronExpression) {
        ArgumentException.ThrowIfNullOrWhiteSpace(cronExpression);

        var parts = cronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var normalizedCronExpression = string.Join(' ', parts);

        return parts.Length switch {
            5 => CrontabSchedule.Parse(normalizedCronExpression),
            6 => CrontabSchedule.Parse(
                normalizedCronExpression,
                new CrontabSchedule.ParseOptions { IncludingSeconds = true }),
            _ => throw new ArgumentException("Cron expression must contain 5 fields, or 6 fields when including seconds.", nameof(cronExpression))
        };
    }

    private void UpdateScheduleInternal() {
        try {
            if (_scheduleConfig.IsEnabled && !string.IsNullOrWhiteSpace(_scheduleConfig.CronExpression)) {
                _schedule = ParseCronExpression(_scheduleConfig.CronExpression);
                _nextExecutionTime = GetNextScheduledTime(DateTime.UtcNow);

                _logger.LogDebug("Schedule updated successfully. Next execution: {NextExecution}", _nextExecutionTime);
            }
            else {
                _schedule = null;
                _nextExecutionTime = null;

                _logger.LogDebug("Scheduled processing disabled");
            }
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to parse cron expression: {CronExpression}", _scheduleConfig.CronExpression);
            _schedule = null;
            _nextExecutionTime = null;
        }
    }

    public override void Dispose() {
        if (_cancellationTokenSource != null) {
            try {
                _cancellationTokenSource.Cancel();
            }
            catch (ObjectDisposedException ex) {
                _logger.LogDebug(ex, "Cancellation token source already disposed during service shutdown");
            }
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }
        _executionSemaphore?.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<bool> CancelCurrentRunAsync() {
        if (_cancellationTokenSource != null) {
            try {
                await _cancellationTokenSource.CancelAsync();
                return true;
            }
            catch (ObjectDisposedException) {
                return false;
            }
        }
        return false;
    }

    private sealed class FileDiscoveryResult {
        public List<DataPipelineRequest> Requests { get; } = [];

        public List<string> SkippedLegacyPdfFiles { get; } = [];
    }
}
