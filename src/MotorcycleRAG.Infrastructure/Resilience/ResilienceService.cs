using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Models;
using Polly;
using Polly.CircuitBreaker;
using System.Diagnostics;

namespace MotorcycleRAG.Infrastructure.Resilience;

/// <summary>
/// Centralized resilience service providing circuit breaker, retry, and fallback mechanisms
/// </summary>
public class ResilienceService
{
    private readonly ILogger<ResilienceService> _logger;
    private readonly ResilienceConfiguration _config;
    private readonly Dictionary<string, IAsyncPolicy> _policies;
    private readonly Dictionary<string, CircuitBreakerState> _circuitStates;

    public ResilienceService(
        IOptions<ResilienceConfiguration> config,
        ILogger<ResilienceService> logger)
    {
        _config = config.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policies = new Dictionary<string, IAsyncPolicy>();
        _circuitStates = new Dictionary<string, CircuitBreakerState>();
        
        InitializePolicies();
    }

    /// <summary>
    /// Executes an operation with resilience policies applied
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        string policyKey,
        Func<Task<T>> operation,
        Func<Task<T>>? fallback = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var activity = Activity.Current;
        correlationId ??= activity?.Id ?? Guid.NewGuid().ToString();
        
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["PolicyKey"] = policyKey
        });

        try
        {
            if (!_policies.TryGetValue(policyKey, out var policy))
            {
                _logger.LogWarning("No resilience policy found for key: {PolicyKey}. Executing without resilience.", policyKey);
                return await operation();
            }

            _logger.LogDebug("Executing operation with resilience policy: {PolicyKey}", policyKey);
            
            var result = await policy.ExecuteAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await operation();
            });
            
            _logger.LogDebug("Operation completed successfully with policy: {PolicyKey}", policyKey);
            return result;
        }
        catch (CircuitBreakerOpenException ex)
        {
            _logger.LogWarning(ex, "Circuit breaker is open for policy: {PolicyKey}. Attempting fallback.", policyKey);
            
            if (fallback != null)
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
            
            throw;
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            _logger.LogError(ex, "Operation failed with policy: {PolicyKey}", policyKey);
            
            if (fallback != null && ShouldUseFallback(ex))
            {
                try
                {
                    var fallbackResult = await fallback();
                    _logger.LogInformation("Fallback executed successfully after failure for policy: {PolicyKey}", policyKey);
                    return fallbackResult;
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "Fallback failed for policy: {PolicyKey}", policyKey);
                    throw;
                }
            }
            
            throw;
        }
    }

    /// <summary>
    /// Executes an operation without return value with resilience policies applied
    /// </summary>
    public async Task ExecuteAsync(
        string policyKey,
        Func<Task> operation,
        Func<Task>? fallback = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            policyKey,
            async () =>
            {
                await operation();
                return true; // Return dummy value for generic method
            },
            fallback != null ? async () =>
            {
                await fallback();
                return true;
            } : null,
            correlationId,
            cancellationToken);
    }

    /// <summary>
    /// Gets the current state of a circuit breaker
    /// </summary>
    public CircuitBreakerState GetCircuitBreakerState(string policyKey)
    {
        return _circuitStates.TryGetValue(policyKey, out var state) 
            ? state 
            : CircuitBreakerState.Closed;
    }

    /// <summary>
    /// Gets health status of all circuit breakers
    /// </summary>
    public Dictionary<string, CircuitBreakerState> GetHealthStatus()
    {
        return new Dictionary<string, CircuitBreakerState>(_circuitStates);
    }

    private void InitializePolicies()
    {
        // Azure OpenAI policy with circuit breaker and retry
        var openAIPolicy = CreateCombinedPolicy(
            "AzureOpenAI",
            _config.CircuitBreaker.OpenAI,
            _config.Retry);
        _policies["AzureOpenAI"] = openAIPolicy;

        // Azure Search policy with circuit breaker and retry
        var searchPolicy = CreateCombinedPolicy(
            "AzureSearch",
            _config.CircuitBreaker.Search,
            _config.Retry);
        _policies["AzureSearch"] = searchPolicy;

        // Document Intelligence policy
        var documentPolicy = CreateCombinedPolicy(
            "DocumentIntelligence",
            _config.CircuitBreaker.DocumentIntelligence,
            _config.Retry);
        _policies["DocumentIntelligence"] = documentPolicy;
    }

    private IAsyncPolicy CreateCombinedPolicy(
        string policyName,
        CircuitBreakerConfiguration circuitConfig,
        RetryConfiguration retryConfig)
    {
        // Retry policy
        var retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .Or<TimeoutException>()
            .OrResult<object>(result => false) // Never retry on successful result
            .WaitAndRetryAsync(
                retryCount: retryConfig.MaxRetries,
                sleepDurationProvider: retryAttempt => retryConfig.UseExponentialBackoff
                    ? TimeSpan.FromSeconds(Math.Min(
                        retryConfig.BaseDelaySeconds * Math.Pow(2, retryAttempt - 1),
                        retryConfig.MaxDelaySeconds))
                    : TimeSpan.FromSeconds(retryConfig.BaseDelaySeconds),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("Retry attempt {RetryCount} for {PolicyName} after {Delay}ms",
                        retryCount, policyName, timespan.TotalMilliseconds);
                });

        // Circuit breaker policy
        var circuitBreakerPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                circuitConfig.FailureThreshold,
                circuitConfig.SamplingDuration,
                onBreak: (exception, duration) =>
                {
                    _circuitStates[policyName] = CircuitBreakerState.Open;
                    _logger.LogWarning("Circuit breaker opened for {PolicyName}. Duration: {Duration}ms. Exception: {Exception}",
                        policyName, duration.TotalMilliseconds, exception.Message);
                },
                onReset: () =>
                {
                    _circuitStates[policyName] = CircuitBreakerState.Closed;
                    _logger.LogInformation("Circuit breaker reset for {PolicyName}", policyName);
                },
                onHalfOpen: () =>
                {
                    _circuitStates[policyName] = CircuitBreakerState.HalfOpen;
                    _logger.LogInformation("Circuit breaker half-open for {PolicyName}", policyName);
                });
        
        // Initialize circuit state
        _circuitStates[policyName] = CircuitBreakerState.Closed;

        // Combine policies: circuit breaker wraps retry
        return Policy.WrapAsync(circuitBreakerPolicy, retryPolicy);
    }

    private static bool ShouldUseFallback(Exception ex)
    {
        return ex is HttpRequestException ||
               ex is TaskCanceledException ||
               ex is TimeoutException ||
               ex is CircuitBreakerOpenException;
    }
}

/// <summary>
/// Circuit breaker state enumeration
/// </summary>
public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}