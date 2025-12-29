using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.DTOs;
using NCrontab;

namespace MotorcycleRAG.Application.Pipeline;

/// <summary>
/// Background service for scheduled processing of data pipelines
/// </summary>
public class ScheduledPipelineService : BackgroundService, IScheduledPipelineService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<ScheduledPipelineService> _logger;
    private readonly ScheduledProcessingConfiguration _config;
    private readonly SemaphoreSlim _executionSemaphore;

    private ProcessingScheduleConfig _scheduleConfig;
    private CrontabSchedule? _schedule;
    private DateTime? _nextExecutionTime;
    private ScheduledProcessingStats _stats;
    private CancellationTokenSource? _cancellationTokenSource;

    public ScheduledPipelineService(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<ScheduledProcessingConfiguration> config,
        ILogger<ScheduledPipelineService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _executionSemaphore = new SemaphoreSlim(1, 1);
        _stats = new ScheduledProcessingStats();

        // Initialize with default schedule
        _scheduleConfig = new ProcessingScheduleConfig
        {
            CronExpression = _config.DefaultCronExpression,
            IsEnabled = _config.IsEnabledByDefault,
            ProcessingWindow = _config.DefaultProcessingWindow,
            MaxConcurrentJobs = _config.DefaultMaxConcurrentJobs
        };

        UpdateScheduleInternal();
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting scheduled pipeline service with cron expression: {CronExpression}", _scheduleConfig.CronExpression);

        if (_scheduleConfig.IsEnabled)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            await base.StartAsync(cancellationToken);
            _logger.LogInformation("Scheduled pipeline service started. Next execution: {NextExecution}", _nextExecutionTime);
        }
        else
        {
            _logger.LogInformation("Scheduled pipeline service is disabled");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping scheduled pipeline service");

        _cancellationTokenSource?.Cancel();
        await base.StopAsync(cancellationToken);

        _logger.LogInformation("Scheduled pipeline service stopped");
    }

    public async Task<PipelineExecutionResult> ExecuteImmediateRunAsync(CancellationToken cancellationToken = default)
    {
        await _executionSemaphore.WaitAsync(cancellationToken);

        try
        {
            _logger.LogInformation("Executing immediate scheduled pipeline run");

            using var scope = _serviceScopeFactory.CreateScope();
            return await ExecuteScheduledProcessingAsync(scope.ServiceProvider, cancellationToken);
        }
        finally
        {
            _executionSemaphore.Release();
        }
    }

    public async Task<DateTime?> GetNextExecutionTimeAsync()
    {
        return await Task.FromResult(_nextExecutionTime);
    }

    public async Task<ScheduledProcessingStats> GetProcessingStatsAsync()
    {
        return await Task.FromResult(_stats);
    }

    public async Task UpdateScheduleAsync(ProcessingScheduleConfig config)
    {
        _scheduleConfig = config ?? throw new ArgumentNullException(nameof(config));
        UpdateScheduleInternal();

        _logger.LogInformation("Schedule updated. New cron expression: {CronExpression}, Enabled: {Enabled}",
            config.CronExpression, config.IsEnabled);

        await Task.CompletedTask;
    }

    public async Task CancelCurrentRunAsync()
    {
        _cancellationTokenSource?.Cancel();
        await Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_scheduleConfig.IsEnabled)
        {
            _logger.LogInformation("Scheduled processing is disabled, service will not execute");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var nextRun = GetNextScheduledTime(now);

                if (nextRun.HasValue)
                {
                    _nextExecutionTime = nextRun.Value;
                    var delay = nextRun.Value - now;

                    if (delay > TimeSpan.Zero)
                    {
                        _logger.LogDebug("Next scheduled execution in {Delay} at {NextRun}", delay, nextRun.Value);
                        await Task.Delay(delay, stoppingToken);
                    }

                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await _executionSemaphore.WaitAsync(stoppingToken);

                        try
                        {
                            using var scope = _serviceScopeFactory.CreateScope();
                            await ExecuteScheduledProcessingAsync(scope.ServiceProvider, stoppingToken);
                        }
                        finally
                        {
                            _executionSemaphore.Release();
                        }
                    }
                }
                else
                {
                    // If no next run time, wait a reasonable interval before checking again
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduled pipeline service execution loop");

                // Wait before retrying to avoid tight error loops
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task<PipelineExecutionResult> ExecuteScheduledProcessingAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var result = new PipelineExecutionResult
        {
            ExecutionId = Guid.NewGuid().ToString(),
            StartTime = startTime,
            Status = PipelineStatus.Processing
        };

        try
        {
            _logger.LogInformation("Starting scheduled pipeline processing at {StartTime}", startTime);

            // Update stats
            _stats.TotalScheduledRuns++;
            _stats.LastExecutionTime = startTime;
            _stats.LastExecutionStatus = PipelineStatus.Processing;

            var orchestrator = serviceProvider.GetRequiredService<IDataPipelineOrchestrator>();

            // Find files to process
            var filesToProcess = await DiscoverFilesToProcessAsync();
            _logger.LogInformation("Found {FileCount} files to process", filesToProcess.Count);

            if (filesToProcess.Count == 0)
            {
                result.Status = PipelineStatus.Completed;
                result.Message = "No files found to process";
                result.EndTime = DateTime.UtcNow;

                _stats.LastExecutionStatus = PipelineStatus.Completed;
                _stats.FilesProcessedInLastRun = 0;

                return result;
            }

            // Process files in batch
            var batchResult = await orchestrator.ProcessBatchAsync(filesToProcess, cancellationToken);

            // Update result based on batch processing
            result.Status = batchResult.HasErrors ? PipelineStatus.PartiallyCompleted : PipelineStatus.Completed;
            result.EndTime = batchResult.EndTime;
            result.Message = $"Processed {batchResult.ProcessedSuccessfully} files successfully, {batchResult.Failed} failed";

            if (batchResult.HasErrors)
            {
                result.Errors.AddRange(batchResult.Results.SelectMany(r => r.Errors));
            }

            // Update statistics
            _stats.LastExecutionStatus = result.Status;
            _stats.FilesProcessedInLastRun = batchResult.TotalFiles;

            if (result.Status == PipelineStatus.Completed)
            {
                _stats.SuccessfulRuns++;
            }
            else
            {
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
        catch (Exception ex)
        {
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

    private async Task<List<DataPipelineRequest>> DiscoverFilesToProcessAsync()
    {
        var requests = new List<DataPipelineRequest>();

        try
        {
            var processingDirectory = Path.Combine(_config.BaseDirectory, _scheduleConfig.ProcessingDirectory);

            if (!Directory.Exists(processingDirectory))
            {
                Directory.CreateDirectory(processingDirectory);
                return requests;
            }

            var files = Directory.GetFiles(processingDirectory, "*.*", SearchOption.TopDirectoryOnly);

            foreach (var filePath in files)
            {
                var fileName = Path.GetFileName(filePath);
                var extension = Path.GetExtension(fileName).ToLowerInvariant();

                var fileType = extension switch
                {
                    ".csv" => FileType.CSV,
                    ".pdf" => FileType.PDF,
                    _ => FileType.Unknown
                };

                if (fileType != FileType.Unknown)
                {
                    requests.Add(new DataPipelineRequest
                    {
                        FileName = fileName,
                        FilePath = filePath,
                        FileType = fileType,
                        CreatedBy = "ScheduledService",
                        Options = new PipelineOptions
                        {
                            IndexImmediately = true,
                            ProcessImages = true,
                            GenerateEmbeddings = true
                        }
                    });
                }
            }

            _logger.LogDebug("Discovered {FileCount} files for processing in {Directory}", requests.Count, processingDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering files to process");
        }

        return requests;
    }

    private DateTime? GetNextScheduledTime(DateTime fromTime)
    {
        try
        {
            if (_schedule == null || !_scheduleConfig.IsEnabled)
                return null;

            return _schedule.GetNextOccurrence(fromTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating next scheduled time with cron expression: {CronExpression}", _scheduleConfig.CronExpression);
            return null;
        }
    }

    private void UpdateScheduleInternal()
    {
        try
        {
            if (_scheduleConfig.IsEnabled && !string.IsNullOrWhiteSpace(_scheduleConfig.CronExpression))
            {
                _schedule = CrontabSchedule.Parse(_scheduleConfig.CronExpression);
                _nextExecutionTime = GetNextScheduledTime(DateTime.UtcNow);

                _logger.LogDebug("Schedule updated successfully. Next execution: {NextExecution}", _nextExecutionTime);
            }
            else
            {
                _schedule = null;
                _nextExecutionTime = null;

                _logger.LogDebug("Scheduled processing disabled");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse cron expression: {CronExpression}", _scheduleConfig.CronExpression);
            _schedule = null;
            _nextExecutionTime = null;
        }
    }

    public override void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _executionSemaphore?.Dispose();
        base.Dispose();
    }

    Task<bool> IScheduledPipelineService.CancelCurrentRunAsync()
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Configuration for scheduled processing
/// </summary>
public class ScheduledProcessingConfiguration
{
    public string DefaultCronExpression { get; set; } = "0 0 2 * * *"; // Daily at 2 AM
    public bool IsEnabledByDefault { get; set; } = true;
    public TimeSpan DefaultProcessingWindow { get; set; } = TimeSpan.FromHours(4);
    public int DefaultMaxConcurrentJobs { get; set; } = 3;
    public string BaseDirectory { get; set; } = "data";
}