using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Services.Dtos;

namespace MotorcycleRAG.Admin.Services;

internal sealed class LocalProcessorService : ILocalProcessorService {
    private const int MaxRecentProcessMessages = 100;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartupPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan HealthReadyTimeout = TimeSpan.FromSeconds(30); // cold Python start needs time for Azure credential init

    private readonly IConfigurationStateService _configurationStateService;
    private readonly ILogger<LocalProcessorService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentQueue<string> _recentProcessMessages = new();
    private readonly string _processorLogDirectory;
    private readonly string _processorLogFilePath;

    private Process? _managedProcess;

    public IReadOnlyList<string> RecentProcessOutput => GetRecentProcessOutput();

    public LocalProcessorService(
        IConfigurationStateService configurationStateService,
        ILogger<LocalProcessorService> logger)
        : this(configurationStateService, logger, GetDefaultLogDirectory()) {
    }

    internal LocalProcessorService(
        IConfigurationStateService configurationStateService,
        ILogger<LocalProcessorService> logger,
        string processorLogDirectory,
        HttpMessageHandler? httpMessageHandler = null) {
        _configurationStateService = configurationStateService ?? throw new ArgumentNullException(nameof(configurationStateService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processorLogDirectory = string.IsNullOrWhiteSpace(processorLogDirectory)
            ? GetDefaultLogDirectory()
            : processorLogDirectory;
        _processorLogFilePath = Path.Combine(_processorLogDirectory, "local-processor.log");
        _httpClient = httpMessageHandler is null
            ? new HttpClient()
            : new HttpClient(httpMessageHandler, disposeHandler: true);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    private static string NormalizeProviderEndpoint(string providerEndpoint) {
        var normalized = providerEndpoint.Trim().TrimEnd('/');

        foreach (var suffix in new[] { "/models", "/embeddings", "/chat/completions", "/api/tags" }) {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                normalized = normalized[..^suffix.Length];
                break;
            }
        }

        return normalized;
    }

    private static IReadOnlyList<string> BuildOpenAiCandidateUrls(string providerEndpoint) {
        var normalized = NormalizeProviderEndpoint(providerEndpoint);
        var candidates = new List<string>();

        if (!normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
            candidates.Add($"{normalized}/v1/models");
        }

        candidates.Add($"{normalized}/models");

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string BuildOllamaCandidateUrl(string providerEndpoint) {
        return $"{NormalizeProviderEndpoint(providerEndpoint)}/api/tags";
    }

    private static IReadOnlyList<string> DistinctModels(IEnumerable<string> models) {
        return models
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string> ParseOpenAiModels(string payload) {
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array) {
            return [];
        }

        return DistinctModels(
            dataElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out _))
                .Select(item => item.GetProperty("id").GetString() ?? string.Empty));
    }

    private static IReadOnlyList<string> ParseOllamaModels(string payload) {
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("models", out var modelsElement) || modelsElement.ValueKind != JsonValueKind.Array) {
            return [];
        }

        return DistinctModels(
            modelsElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => {
                    if (item.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String) {
                        return modelElement.GetString() ?? string.Empty;
                    }

                    if (item.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String) {
                        return nameElement.GetString() ?? string.Empty;
                    }

                    return string.Empty;
                }));
    }

    private async Task<EmbeddingModelDiscoveryResponse> DiscoverEmbeddingModelsAsync(string providerEndpoint, CancellationToken cancellationToken) {
        Exception? lastError = null;
        var normalizedEndpoint = NormalizeProviderEndpoint(providerEndpoint);

        foreach (var candidateUrl in BuildOpenAiCandidateUrls(providerEndpoint)) {
            foreach (var sendBearerHeader in new[] { false, true }) {
                try {
                    using var request = new HttpRequestMessage(HttpMethod.Get, candidateUrl);
                    if (sendBearerHeader) {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "local");
                    }

                    using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    var models = ParseOpenAiModels(payload);
                    if (models.Count > 0) {
                        var resolvedEndpoint = candidateUrl[..^"/models".Length];
                        return new EmbeddingModelDiscoveryResponse {
                            Provider = "openai-compatible",
                            Endpoint = resolvedEndpoint,
                            Models = models.ToList()
                        };
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                }
                catch (Exception ex) {
                    lastError = ex;
                }
            }
        }

        try {
            using var response = await _httpClient.GetAsync(new Uri(BuildOllamaCandidateUrl(providerEndpoint)), cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var models = ParseOllamaModels(payload);
            if (models.Count > 0) {
                var resolvedEndpoint = BuildOllamaCandidateUrl(providerEndpoint)[..^"/api/tags".Length];
                return new EmbeddingModelDiscoveryResponse {
                    Provider = "ollama",
                    Endpoint = resolvedEndpoint,
                    Models = models.ToList()
                };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception ex) {
            lastError = ex;
        }

        throw new InvalidOperationException(
            $"Unable to detect an embedding model provider at {normalizedEndpoint}.",
            lastError);
    }

    public async Task<LocalProcessorHealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default) {
        var endpoint = GetEndpoint();
        if (endpoint is null) {
            return null;
        }

        if (endpoint.IsLoopback && !IsEndpointPortInUse(endpoint)) {
            _logger.LogTrace("Skipping local processor health check because {Endpoint} has no active listeners", endpoint);
            return null;
        }

        try {
            using var response = await _httpClient.GetAsync(new Uri(endpoint, "/health"), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                _logger.LogDebug("Local processor health check returned non-success status {StatusCode} for {Endpoint}", response.StatusCode, endpoint);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<LocalProcessorHealthResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) {
            _logger.LogTrace("Local processor health check failed with HTTP exception for {Endpoint}", endpoint);
            return null;
        }
        catch (TaskCanceledException) {
            _logger.LogTrace("Local processor health check timed out for {Endpoint}", endpoint);
            return null;
        }
    }

    public async Task<EmbeddingModelDiscoveryResponse> GetEmbeddingModelsAsync(string providerEndpoint, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(providerEndpoint)) {
            throw new ArgumentException("An embedding provider endpoint is required.", nameof(providerEndpoint));
        }

        if (!Uri.TryCreate(providerEndpoint, UriKind.Absolute, out _)) {
            throw new ArgumentException("Invalid embedding provider endpoint format.", nameof(providerEndpoint));
        }

        return await DiscoverEmbeddingModelsAsync(providerEndpoint, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LocalProcessorJobResponse>> GetJobsAsync(CancellationToken cancellationToken = default) {
        var endpoint = GetEndpoint();
        using var response = await _httpClient.GetAsync(new Uri(endpoint, "/jobs"), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<LocalProcessorJobResponse>>(cancellationToken: cancellationToken).ConfigureAwait(false)
               ?? [];
    }

    public async Task<int> ClearFinishedJobsAsync(CancellationToken cancellationToken = default) {
        var endpoint = GetEndpoint();
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, new Uri(endpoint, "/jobs"));
        using var deleteResponse = await _httpClient.SendAsync(deleteRequest, cancellationToken).ConfigureAwait(false);
        if (deleteResponse.StatusCode == HttpStatusCode.MethodNotAllowed) {
            return await ClearFinishedJobsWithPostFallbackAsync(endpoint, cancellationToken).ConfigureAwait(false);
        }

        if (deleteResponse.StatusCode == HttpStatusCode.NotFound) {
            throw new InvalidOperationException("The local processor cleanup endpoint is unavailable. Restart the local processor and try again.");
        }

        deleteResponse.EnsureSuccessStatusCode();

        return await ReadDeletedJobCountAsync(deleteResponse, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ClearFinishedJobsWithPostFallbackAsync(Uri endpoint, CancellationToken cancellationToken) {
        using var postResponse = await _httpClient.PostAsync(new Uri(endpoint, "/jobs/cleanup"), content: null, cancellationToken).ConfigureAwait(false);
        if (postResponse.StatusCode == HttpStatusCode.MethodNotAllowed || postResponse.StatusCode == HttpStatusCode.NotFound) {
            throw new InvalidOperationException("The local processor is running an older API version. Restart it and try clearing finished jobs again.");
        }

        postResponse.EnsureSuccessStatusCode();

        return await ReadDeletedJobCountAsync(postResponse, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> ReadDeletedJobCountAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        var payload = await response.Content.ReadFromJsonAsync<LocalProcessorJobCleanupResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        return payload?.DeletedCount ?? 0;
    }

    public async Task<LocalProcessorHealthResponse> StartAsync(CancellationToken cancellationToken = default) {
        var endpoint = GetEndpoint();
        var currentHealth = await GetHealthAsync(cancellationToken).ConfigureAwait(false);
        if (currentHealth is not null) {
            return currentHealth;
        }

        if (IsEndpointPortInUse(endpoint)) {
            throw new InvalidOperationException(
                $"Port {endpoint.Port} is already in use, but {endpoint} did not respond to /health. Stop the other process or change the local processor endpoint.");
        }

        var workingDirectory = GetWorkingDirectory();
        var startCommand = GetStartCommand();
        var startupSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        ClearRecentProcessMessages();

        _managedProcess = BuildProcess(startCommand, workingDirectory, startupSignal);
        _managedProcess.Start();
        _managedProcess.BeginOutputReadLine();
        _managedProcess.BeginErrorReadLine();

        var startedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - startedAt < StartupTimeout) {
            cancellationToken.ThrowIfCancellationRequested();

            if (_managedProcess.HasExited) {
                // Wait briefly for asynchronous output streams (OutputDataReceived/ErrorDataReceived) to flush
                try {
                    using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    flushCts.CancelAfter(TimeSpan.FromMilliseconds(500));
                    await _managedProcess.WaitForExitAsync(flushCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    // Ignore timeout, we just wanted to wait a little bit for logs to flush
                }

                throw CreateStartupFailure(_managedProcess, endpoint);
            }

            if (startupSignal.Task.IsCompletedSuccessfully) {
                break;
            }

            await Task.Delay(StartupPollInterval, cancellationToken).ConfigureAwait(false);
        }

        if (!startupSignal.Task.IsCompletedSuccessfully) {
            throw new TimeoutException(BuildStartupTimeoutMessage(endpoint));
        }

        var healthReadyStartedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - healthReadyStartedAt < HealthReadyTimeout) {
            cancellationToken.ThrowIfCancellationRequested();

            if (_managedProcess.HasExited) {
                // Wait briefly for asynchronous output streams (OutputDataReceived/ErrorDataReceived) to flush
                try {
                    using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    flushCts.CancelAfter(TimeSpan.FromMilliseconds(500));
                    await _managedProcess.WaitForExitAsync(flushCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) {
                    // Ignore
                }

                throw CreateStartupFailure(_managedProcess, endpoint);
            }

            // Use a short per-request timeout so the loop can retry quickly across the full window.
            using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            pollCts.CancelAfter(TimeSpan.FromSeconds(2));
            try {
                var health = await GetHealthAsync(pollCts.Token).ConfigureAwait(false);
                if (health is not null) {
                    return health;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                // Per-request timeout — server not ready yet, keep polling
            }

            await Task.Delay(StartupPollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException(BuildStartupTimeoutMessage(endpoint));
    }

    public async Task StopAsync(CancellationToken cancellationToken = default) {
        var endpoint = GetEndpoint();
        var health = await GetHealthAsync(cancellationToken).ConfigureAwait(false);
        if (health is null) {
            return;
        }

        using var response = await _httpClient.PostAsync(new Uri(endpoint, "/control/shutdown"), content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        _ = await response.Content.ReadFromJsonAsync<LocalProcessorShutdownResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);

        var stopRequestedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - stopRequestedAt < ShutdownTimeout) {
            cancellationToken.ThrowIfCancellationRequested();

            var currentHealth = await GetHealthAsync(cancellationToken).ConfigureAwait(false);
            if (currentHealth is null) {
                return;
            }

            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("Timed out waiting for the local processor to stop gracefully.");
    }

    public async Task<LocalGraphJobStartResult> StartBikeGraphJobAsync(string localFilePath, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(localFilePath)) {
            throw new ArgumentException("A CSV file path is required.", nameof(localFilePath));
        }

        var health = await GetHealthAsync(cancellationToken).ConfigureAwait(false);
        if (health is null) {
            throw new InvalidOperationException("The local processor is not running.");
        }

        if (!health.AcceptingWork) {
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
        if (payload == null || string.IsNullOrWhiteSpace(payload.JobId)) {
            throw new InvalidOperationException("The local processor did not return a job identifier.");
        }

        return new LocalGraphJobStartResult {
            UploadId = uploadId,
            JobId = payload.JobId,
            Status = payload.Status,
            Message = payload.Message
        };
    }

    internal IReadOnlyDictionary<string, string> BuildChildEnvironmentVariables() {
        Directory.CreateDirectory(_processorLogDirectory);

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["PYTHONUNBUFFERED"] = "1",
            ["LOCAL_PROCESSOR_LOG_DIR"] = _processorLogDirectory
        };

        if (!string.IsNullOrWhiteSpace(_configurationStateService.EmbeddingProviderEndpoint)) {
            variables["EMBEDDING_PROVIDER_ENDPOINT"] = _configurationStateService.EmbeddingProviderEndpoint;
        }

        if (!string.IsNullOrWhiteSpace(_configurationStateService.EmbeddingModel)) {
            variables["EMBEDDING_MODEL"] = _configurationStateService.EmbeddingModel;
        }

        if (_configurationStateService.PdfChunkerMaxTokens > 0) {
            variables["PDF_CHUNKER_MAX_TOKENS"] = _configurationStateService.PdfChunkerMaxTokens.ToString();
        }

        if (_configurationStateService.CsvChunkMaxTokens > 0) {
            variables["MAX_CHUNK_SIZE_TOKENS"] = _configurationStateService.CsvChunkMaxTokens.ToString();
        }

        if (!string.IsNullOrWhiteSpace(_configurationStateService.PdfChunkerTokenizer)) {
            variables["PDF_CHUNKER_TOKENIZER"] = _configurationStateService.PdfChunkerTokenizer;
        }

        if (!string.IsNullOrWhiteSpace(_configurationStateService.LocalProcessorUploadJobSecret)) {
            variables["PYTHON_UPLOAD_JOB_SECRET"] = _configurationStateService.LocalProcessorUploadJobSecret;
        }

        var apiBaseUrl = _configurationStateService.ApiBaseUrl?.AbsoluteUri.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(apiBaseUrl)) {
            variables["MCR_API_BASE_URL"] = apiBaseUrl;
        }

        return variables;
    }

    private static async Task<string?> ReadErrorDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content)) {
            return null;
        }

        try {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("detail", out var detailElement)
                && detailElement.ValueKind == JsonValueKind.String) {
                return detailElement.GetString();
            }
        }
        catch (JsonException) {
        }

        return content;
    }

    private Process BuildProcess(string startCommand, string workingDirectory, TaskCompletionSource<bool> startupSignal) {
        var startInfo = new ProcessStartInfo {
            FileName = "cmd.exe",
            Arguments = $"/c {startCommand}",
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var variable in BuildChildEnvironmentVariables()) {
            startInfo.EnvironmentVariables[variable.Key] = variable.Value;
        }

        var process = new Process {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, e) => {
            if (!string.IsNullOrWhiteSpace(e.Data)) {
                TrackProcessMessage(e.Data);
                _logger.LogInformation("Local processor: {Message}", e.Data);
                if (IsStartupReadyMessage(e.Data)) {
                    startupSignal.TrySetResult(true);
                }
            }
        };

        process.ErrorDataReceived += (_, e) => {
            if (!string.IsNullOrWhiteSpace(e.Data)) {
                TrackProcessMessage(e.Data);
                _logger.LogInformation("Local processor stderr: {Message}", e.Data);
            }
        };

        process.Exited += (_, _) => _logger.LogInformation("Local processor exited with code {ExitCode}", process.ExitCode);
        return process;
    }

    private Uri GetEndpoint() {
        if (_configurationStateService.LocalProcessorEndpoint == null) {
            throw new InvalidOperationException("Local processor endpoint is not configured. Open Settings to configure it.");
        }

        return _configurationStateService.LocalProcessorEndpoint;
    }

    private string GetWorkingDirectory() {
        var workingDirectory = _configurationStateService.LocalProcessorWorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory)) {
            throw new InvalidOperationException("Local processor working directory is not configured. Open Settings to configure it.");
        }

        if (!Directory.Exists(workingDirectory)) {
            throw new DirectoryNotFoundException($"The local processor directory '{workingDirectory}' was not found.");
        }

        if (!File.Exists(Path.Combine(workingDirectory, "src", "main.py"))) {
            throw new FileNotFoundException("The local processor entry point src/main.py was not found.", Path.Combine(workingDirectory, "src", "main.py"));
        }

        return workingDirectory;
    }

    private string GetStartCommand() {
        var startCommand = _configurationStateService.LocalProcessorStartCommand;
        if (string.IsNullOrWhiteSpace(startCommand)) {
            throw new InvalidOperationException("Local processor start command is not configured. Open Settings to configure it.");
        }

        return startCommand;
    }

    private static bool IsStartupReadyMessage(string message) {
        return message.Contains("Application startup complete.", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Uvicorn running on", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsErrorKeyword(string message) {
        return message.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Traceback", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearRecentProcessMessages() {
        while (_recentProcessMessages.TryDequeue(out _)) {
        }
    }

    private void TrackProcessMessage(string message) {
        _recentProcessMessages.Enqueue(message);
        while (_recentProcessMessages.Count > MaxRecentProcessMessages && _recentProcessMessages.TryDequeue(out _)) {
        }
    }

    private IReadOnlyList<string> GetRecentProcessOutput() {
        var trackedOutput = _recentProcessMessages.ToArray();
        if (trackedOutput.Length > 0) {
            return trackedOutput;
        }

        return ReadRecentLogLines();
    }

    private IReadOnlyList<string> ReadRecentLogLines() {
        if (!File.Exists(_processorLogFilePath)) {
            return [];
        }

        try {
            return File.ReadLines(_processorLogFilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .TakeLast(MaxRecentProcessMessages)
                .ToArray();
        }
        catch (IOException ex) {
            _logger.LogTrace(ex, "Unable to read local processor log file {LogFilePath}", _processorLogFilePath);
            return [];
        }
        catch (UnauthorizedAccessException ex) {
            _logger.LogTrace(ex, "Access denied while reading local processor log file {LogFilePath}", _processorLogFilePath);
            return [];
        }
    }

    private static string GetDefaultLogDirectory() {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MotorcycleRAGAdmin",
            "logs");
    }

    private Exception CreateStartupFailure(Process process, Uri endpoint) {
        var recentMessages = string.Join(Environment.NewLine, _recentProcessMessages.ToArray());
        if (recentMessages.Contains("error while attempting to bind", StringComparison.OrdinalIgnoreCase) ||
            recentMessages.Contains("only one usage of each socket address", StringComparison.OrdinalIgnoreCase)) {
            return new InvalidOperationException(
                $"Port {endpoint.Port} is already in use, so the local processor could not bind to {endpoint}.{Environment.NewLine}{Environment.NewLine}Recent output:{Environment.NewLine}{recentMessages}".Trim());
        }

        var message = string.IsNullOrWhiteSpace(recentMessages)
            ? $"The local processor exited with code {process.ExitCode}."
            : $"The local processor exited with code {process.ExitCode}.{Environment.NewLine}{Environment.NewLine}Recent output:{Environment.NewLine}{recentMessages}";

        return new InvalidOperationException(message.Trim());
    }

    private string BuildStartupTimeoutMessage(Uri endpoint) {
        var recentMessages = string.Join(Environment.NewLine, _recentProcessMessages.ToArray());
        return string.IsNullOrWhiteSpace(recentMessages)
            ? $"Timed out waiting for the local processor to start at {endpoint}."
            : $"Timed out waiting for the local processor to start at {endpoint}.{Environment.NewLine}{Environment.NewLine}Recent output:{Environment.NewLine}{recentMessages}";
    }

    private static bool IsEndpointPortInUse(Uri endpoint) {
        if (!endpoint.IsLoopback) {
            return false;
        }

        try {
            return IPGlobalProperties
                .GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(listener => listener.Port == endpoint.Port && IsLoopbackOrAnyAddress(listener.Address));
        }
        catch (SocketException) {
            return false;
        }
        catch (NetworkInformationException) {
            return false;
        }
    }

    private static bool IsLoopbackOrAnyAddress(IPAddress address) {
        return IPAddress.IsLoopback(address) ||
               address.Equals(IPAddress.Any) ||
               address.Equals(IPAddress.IPv6Any);
    }
}
