using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Models;
using Polly;
using Polly.CircuitBreaker;
using System.Diagnostics;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.Models;
using MotorcycleRAG.Contracts.Options;

namespace MotorcycleRAG.Persistence.Resilience;

public class ResilienceService : IResilienceService
{
    private readonly ILogger<ResilienceService> _logger;
    private readonly ResilienceOptions _config;
    private readonly Dictionary<string, IAsyncPolicy> _policies;
    private readonly Dictionary<string, CircuitBreakerState> _circuitStates;

    public ResilienceService(
        IOptions<ResilienceOptions> config,
        ILogger<ResilienceService> logger)
    {
        _config = config.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policies = new Dictionary<string, IAsyncPolicy>();
        _circuitStates = new Dictionary<string, CircuitBreakerState>();
        InitializePolicies();
    }

    // Unified ExecuteAsync (generic)
    public async Task<T> ExecuteAsync<T>(string policyKey, Func<Task<T>> operation, Func<Task<T>>? fallback = null, string? correlationId = null, CancellationToken cancellationToken = default)
    {
        var activity = Activity.Current;
        correlationId ??= activity?.Id ?? Guid.NewGuid().ToString();
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId, ["PolicyKey"] = policyKey });
        try
        {
            if (!_policies.TryGetValue(policyKey, out var policy))
            {
                _logger.LogWarning("No resilience policy found for key: {PolicyKey}. Executing without resilience.", policyKey);
                return await operation();
            }
            _logger.LogDebug("Executing operation with resilience policy: {PolicyKey}", policyKey);
            var result = await policy.ExecuteAsync(async () => { cancellationToken.ThrowIfCancellationRequested(); return await operation(); });
            _logger.LogDebug("Operation completed successfully with policy: {PolicyKey}", policyKey);
            return result;
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex, "Circuit breaker open for policy: {PolicyKey}. Attempting fallback.", policyKey);
            if (fallback != null) return await ExecuteFallbackAsync(fallback, policyKey);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Operation failed with policy: {PolicyKey}", policyKey);
            if (fallback != null && ShouldUseFallback(ex)) return await ExecuteFallbackAsync(fallback, policyKey);
            throw;
        }
    }

    // Unified ExecuteAsync (void)
    public async Task ExecuteAsync(string policyKey, Func<Task> operation, Func<Task>? fallback = null, string? correlationId = null, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(policyKey, async () => { await operation(); return true; }, fallback != null ? async () => { await fallback(); return true; } : null, correlationId, cancellationToken);
    }

    // Backwards-compatible simple wrappers required by interface
    public Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName)
        => ExecuteAsync("RetryOnly", operation, null, operationName);

    public Task<T> ExecuteWithCircuitBreakerAsync<T>(Func<Task<T>> operation, string operationName)
        => ExecuteAsync("CircuitBreakerOnly", operation, null, operationName);

    public async Task<T> ExecuteWithTimeoutAsync<T>(Func<Task<T>> operation, TimeSpan timeout, string operationName)
    {
        using var cts = new CancellationTokenSource(timeout);
        return await ExecuteAsync("TimeoutOnly", operation, null, operationName, cts.Token);
    }

    private async Task<T> ExecuteFallbackAsync<T>(Func<Task<T>> fallback, string policyKey)
    {
        try
        {
            var fallbackResult = await fallback();
            _logger.LogInformation("Fallback executed successfully for policy: {PolicyKey}", policyKey);
            return fallbackResult;
        }
        catch (Exception fallbackEx)
        {
            _logger.LogError(fallbackEx, "Fallback failed for policy: {PolicyKey}", policyKey);
            throw;
        }
    }

    public CircuitBreakerState GetCircuitBreakerState(string policyKey) => _circuitStates.TryGetValue(policyKey, out var state) ? state : CircuitBreakerState.Closed;
    public Dictionary<string, CircuitBreakerState> GetHealthStatus() => new(_circuitStates);

    private void InitializePolicies()
    {
        // Minimal policies for legacy keys to satisfy wrappers; map to OpenAI defaults when not explicitly configured
        _policies["AzureOpenAI"] = CreateCombinedPolicy("AzureOpenAI", _config.CircuitBreaker.OpenAI, _config.Retry);
        _policies["AzureSearch"] = CreateCombinedPolicy("AzureSearch", _config.CircuitBreaker.Search, _config.Retry);
        _policies["DocumentIntelligence"] = CreateCombinedPolicy("DocumentIntelligence", _config.CircuitBreaker.DocumentIntelligence, _config.Retry);

        // Basic retry-only/circuit-only/timeout-only placeholders reuse OpenAI policy for simplicity
        _policies["RetryOnly"] = _policies["AzureOpenAI"];
        _policies["CircuitBreakerOnly"] = _policies["AzureOpenAI"];
        _policies["TimeoutOnly"] = _policies["AzureOpenAI"];
    }

    private IAsyncPolicy CreateCombinedPolicy(string policyName, ServiceCircuitBreakerConfig circuitConfig, RetryOptions retryConfig)
    {
        var retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryConfig.MaxRetries,
                attempt => retryConfig.UseExponentialBackoff
                    ? TimeSpan.FromSeconds(Math.Min(retryConfig.BaseDelaySeconds * Math.Pow(2, attempt - 1), retryConfig.MaxDelaySeconds))
                    : TimeSpan.FromSeconds(retryConfig.BaseDelaySeconds),
                (outcome, span, retryCount, ctx) => _logger.LogWarning("Retry attempt {RetryCount} for {PolicyName} after {Delay}ms", retryCount, policyName, span.TotalMilliseconds));

        var circuitBreakerPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                circuitConfig.FailureThreshold,
                circuitConfig.SamplingDuration,
                onBreak: (ex, duration) => { _circuitStates[policyName] = CircuitBreakerState.Open; _logger.LogWarning("Circuit breaker opened for {PolicyName}. Duration: {Duration}ms. Exception: {Exception}", policyName, duration.TotalMilliseconds, ex.Message); },
                onReset: () => { _circuitStates[policyName] = CircuitBreakerState.Closed; _logger.LogInformation("Circuit breaker reset for {PolicyName}", policyName); },
                onHalfOpen: () => { _circuitStates[policyName] = CircuitBreakerState.HalfOpen; _logger.LogInformation("Circuit breaker half-open for {PolicyName}", policyName); });

        _circuitStates[policyName] = CircuitBreakerState.Closed;
        return Policy.WrapAsync(circuitBreakerPolicy, retryPolicy);
    }

    private static bool ShouldUseFallback(Exception ex) => ex is HttpRequestException or TaskCanceledException or TimeoutException or BrokenCircuitException;
}

