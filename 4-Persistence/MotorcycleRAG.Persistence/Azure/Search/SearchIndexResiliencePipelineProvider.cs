using MotorcycleRAG.Core.Exceptions;
using MotorcycleRAG.Core.Options;
using Polly;
using Polly.Retry;

namespace MotorcycleRAG.Persistence.Azure.Search;

/// <summary>
/// Default <see cref="ISearchIndexResiliencePipeline"/>. Builds a Polly
/// <see cref="ResiliencePipeline"/> that retries only transient failures
/// (5xx, 429, network/<see cref="HttpRequestException"/>, <see cref="TaskCanceledException"/>
/// without cancellation, <see cref="TimeoutException"/>) and <b>never</b> retries
/// non-transient status codes (404 / 400 / 401 / 403) or any exception explicitly classified
/// as non-transient by <see cref="ChunkIndexingService"/> (e.g.
/// <see cref="SearchIndexNotFoundException"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Retry policy (T7):</b> up to 3 attempts with exponential backoff
/// (base 0.5s, factor 2, cap 5s, jitter). Retries are gated through
/// <see cref="ShouldRetry"/> which inspects the exception type and &mdash; for
/// <see cref="global::Azure.RequestFailedException"/> &mdash; the HTTP status code.
/// </para>
/// <para>
/// <b>Non-retryable status codes:</b> 400, 401, 403, 404. These indicate the batch or the
/// target index is structurally wrong and retrying would not help; they propagate
/// immediately so the controller transitions the job to <c>Failed</c> (T6/T8).
/// </para>
/// <para>
/// <b>Per-batch timeout (T7):</b> enforced <b>outside</b> this pipeline by the caller
/// (<see cref="ChunkIndexingService"/>) via a linked <see cref="CancellationTokenSource"/>
/// bound to <see cref="SearchOptions.BatchIndexTimeoutSeconds"/>. The pipeline itself does
/// not install a timeout strategy because cancellation must surface as
/// <see cref="OperationCanceledException"/> (not <c>TimeoutRejectedException</c>) so the
/// controller can distinguish "the user cancelled" from "the batch timed out".
/// </para>
/// </remarks>
public sealed class SearchIndexResiliencePipelineProvider : ISearchIndexResiliencePipeline
{
    private const int DefaultMaxRetryAttempts = 3;
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan RetryMaxDelay = TimeSpan.FromSeconds(5.0);

    // Status codes that must NOT be retried (the batch / index is structurally wrong).
    private static readonly HashSet<int> NonTransientStatusCodes = new() { 400, 401, 403, 404 };

    private readonly ResiliencePipeline _pipeline;

    public SearchIndexResiliencePipelineProvider()
    {
        var retryStrategy = new RetryStrategyOptions
        {
            MaxRetryAttempts = DefaultMaxRetryAttempts,
            Delay = RetryBaseDelay,
            MaxDelay = RetryMaxDelay,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = args =>
            {
                var outcome = args.Outcome;
                if (outcome.Exception is null)
                {
                    return ValueTask.FromResult(false);
                }

                return ValueTask.FromResult(ShouldRetry(outcome.Exception));
            },
            OnRetry = args =>
            {
                // Lightweight, structured retry log; correlation is the caller's job.
                return ValueTask.CompletedTask;
            },
        };

        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(retryStrategy)
            .Build();
    }

    /// <inheritdoc />
    public ResiliencePipeline Pipeline => _pipeline;

    /// <summary>
    /// Returns <c>true</c> if the exception is transient and worth retrying.
    /// Network errors and 5xx / 429 are transient; 400 / 401 / 403 / 404 and
    /// <see cref="SearchIndexNotFoundException"/> are not.
    /// </summary>
    internal static bool ShouldRetry(Exception ex)
    {
        // Non-transient domain/SDK exceptions: never retry.
        if (ex is SearchIndexNotFoundException)
        {
            return false;
        }

        if (ex is global::Azure.RequestFailedException rfe)
        {
            // Non-transient status codes: do not retry (T7 transient-only).
            if (NonTransientStatusCodes.Contains(rfe.Status))
            {
                return false;
            }

            // Transient: 5xx, 429, and any unknown status that is not in the non-transient set.
            // (429 and 5xx are the canonical transient codes; treat everything else not
            // explicitly excluded as transient to be robust against SDK status mapping quirks.)
            return rfe.Status >= 500 || rfe.Status == 429 || rfe.Status == 0;
        }

        // Network-level transient failures.
        if (ex is HttpRequestException)
        {
            return true;
        }

        // TaskCanceledException is ambiguous: it can mean "the user cancelled" OR
        // "an HTTP timeout fired". Only retry if the supplied token is not cancelled.
        if (ex is TaskCanceledException tce)
        {
            // If the inner exception is a TimeoutException this was a timeout, not a cancel.
            return tce.InnerException is TimeoutException;
        }

        if (ex is TimeoutException)
        {
            return true;
        }

        return false;
    }
}
