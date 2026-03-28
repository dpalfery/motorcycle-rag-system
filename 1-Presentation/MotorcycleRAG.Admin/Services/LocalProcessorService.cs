using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Services.Dtos;

namespace MotorcycleRAG.Admin.Services;

internal sealed class LocalProcessorService : ILocalProcessorService
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromMinutes(5);

    private readonly IConfigurationStateService _configurationStateService;
    private readonly ILogger<LocalProcessorService> _logger;
    private readonly HttpClient _httpClient;

    private Process? _managedProcess;
    private string _lastProcessError = string.Empty;

    public LocalProcessorService(
        IConfigurationStateService configurationStateService,
        ILogger<LocalProcessorService> logger)
    {
        _configurationStateService = configurationStateService ?? throw new ArgumentNullException(nameof(configurationStateService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = new HttpClient {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task<LocalProcessorHealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = GetEndpoint();
        try
        {
            using var response = await _httpClient.GetAsync(new Uri(endpoint, "/health"), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<LocalProcessorHealthResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<LocalProcessorJobResponse>> GetJobsAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = GetEndpoint();
        using var response = await _httpClient.GetAsync(new Uri(endpoint, "/jobs"), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<LocalProcessorJobResponse>>(cancellationToken: cancellationToken).ConfigureAwait(false)
               ?? [];
    }

    public async Task<int> ClearFinishedJobsAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = GetEndpoint();
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, new Uri(endpoint, "/jobs"));
        using var deleteResponse = await _httpClient.SendAsync(deleteRequest, cancellationToken).ConfigureAwait(false);
        if (deleteResponse.StatusCode == HttpStatusCode.MethodNotAllowed)
        {
            return await ClearFinishedJobsWithPostFallbackAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }

        if (deleteResponse.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("The local processor cleanup endpoint is unavailable. Restart the local processor and try again.");
        }

        deleteResponse.EnsureSuccessStatusCode();

        return await ReadDeletedJobCountAsync(deleteResponse, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ClearFinishedJobsWithPostFallbackAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        using var postResponse = await _httpClient.PostAsync(new Uri(endpoint, "/jobs/cleanup"), content: null, cancellationToken).ConfigureAwait(false);
        if (postResponse.StatusCode == HttpStatusCode.MethodNotAllowed || postResponse.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("The local processor is running an older API version. Restart it and try clearing finished jobs again.");
        }

        postResponse.EnsureSuccessStatusCode();

        return await ReadDeletedJobCountAsync(postResponse, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadDeletedJobCountAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<LocalProcessorJobCleanupResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        return payload?.DeletedCount ?? 0;
    }

    public async Task<LocalProcessorHealthResponse> StartAsync(CancellationToken cancellationToken = default)
    {
        var currentHealth = await GetHealthAsync(cancellationToken).ConfigureAwait(false);
        if (currentHealth is not null)
        {
            return currentHealth;
        }

        var workingDirectory = GetWorkingDirectory();
        var startCommand = GetStartCommand();

        _managedProcess = BuildProcess(startCommand, workingDirectory);
        _managedProcess.Start();
        _managedProcess.BeginOutputReadLine();
        _managedProcess.BeginErrorReadLine();

        var startedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - startedAt < StartupTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_managedProcess.HasExited)
            {
                throw new InvalidOperationException(
                    $"The local processor exited with code {_managedProcess.ExitCode}. {_lastProcessError}".Trim());
            }

            var health = await GetHealthAsync(cancellationToken).ConfigureAwait(false);
            if (health is not null)
            {
                return health;
            }

            await Task.Delay(750, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("Timed out waiting for the local processor to start.");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = GetEndpoint();
        var health = await GetHealthAsync(cancellationToken).ConfigureAwait(false);
        if (health is null)
        {
            return;
        }

        using var response = await _httpClient.PostAsync(new Uri(endpoint, "/control/shutdown"), content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        _ = await response.Content.ReadFromJsonAsync<LocalProcessorShutdownResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);

        var stopRequestedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - stopRequestedAt < ShutdownTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentHealth = await GetHealthAsync(cancellationToken).ConfigureAwait(false);
            if (currentHealth is null)
            {
                return;
            }

            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("Timed out waiting for the local processor to stop gracefully.");
    }

    public async Task<LocalGraphJobStartResult> StartBikeGraphJobAsync(string localFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localFilePath))
        {
            throw new ArgumentException("A CSV file path is required.", nameof(localFilePath));
        }

        var health = await GetHealthAsync(cancellationToken).ConfigureAwait(false);
        if (health is null)
        {
            throw new InvalidOperationException("The local processor is not running.");
        }

        if (!health.AcceptingWork)
        {
            throw new InvalidOperationException("The local processor is shutting down and is not accepting new work.");
        }

        var uploadId = Guid.NewGuid().ToString();
        var request = new LocalGraphJobStartRequest {
            UploadId = uploadId,
            LocalFilePath = localFilePath
        };

        using var response = await _httpClient.PostAsJsonAsync(
            new Uri(GetEndpoint(), "/process/bike-graph"),
            request,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<LocalProcessorJobResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (payload == null || string.IsNullOrWhiteSpace(payload.JobId))
        {
            throw new InvalidOperationException("The local processor did not return a job identifier.");
        }

        return new LocalGraphJobStartResult {
            UploadId = uploadId,
            JobId = payload.JobId,
            Status = payload.Status,
            Message = payload.Message
        };
    }

    private Process BuildProcess(string startCommand, string workingDirectory)
    {
        var process = new Process {
            StartInfo = new ProcessStartInfo {
                FileName = "cmd.exe",
                Arguments = $"/c {startCommand}",
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, e) => {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _logger.LogInformation("Local processor: {Message}", e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) => {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _lastProcessError = e.Data;
                _logger.LogWarning("Local processor stderr: {Message}", e.Data);
            }
        };

        process.Exited += (_, _) => _logger.LogInformation("Local processor exited with code {ExitCode}", process.ExitCode);
        return process;
    }

    private Uri GetEndpoint()
    {
        if (_configurationStateService.LocalProcessorEndpoint == null)
        {
            throw new InvalidOperationException("Local processor endpoint is not configured. Open Settings to configure it.");
        }

        return _configurationStateService.LocalProcessorEndpoint;
    }

    private string GetWorkingDirectory()
    {
        var workingDirectory = _configurationStateService.LocalProcessorWorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new InvalidOperationException("Local processor working directory is not configured. Open Settings to configure it.");
        }

        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException($"The local processor directory '{workingDirectory}' was not found.");
        }

        if (!File.Exists(Path.Combine(workingDirectory, "src", "main.py")))
        {
            throw new FileNotFoundException("The local processor entry point src/main.py was not found.", Path.Combine(workingDirectory, "src", "main.py"));
        }

        return workingDirectory;
    }

    private string GetStartCommand()
    {
        var startCommand = _configurationStateService.LocalProcessorStartCommand;
        if (string.IsNullOrWhiteSpace(startCommand))
        {
            throw new InvalidOperationException("Local processor start command is not configured. Open Settings to configure it.");
        }

        return startCommand;
    }
}
