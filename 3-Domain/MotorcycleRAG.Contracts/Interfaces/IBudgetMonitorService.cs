namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Tracks and enforces monthly Azure spend limits for the ingestion pipeline.
/// Implementations must be thread-safe.
/// </summary>
public interface IBudgetMonitorService
{
    /// <summary>
    /// Returns <c>true</c> if adding <paramref name="amountToSpend"/> to the current
    /// month's accumulated spend would remain at or below the configured monthly budget limit.
    /// </summary>
    /// <param name="amountToSpend">The prospective spend amount to evaluate.</param>
    bool IsWithinBudget(decimal amountToSpend);

    /// <summary>
    /// Atomically records a spend event for the current calendar month.
    /// Implementations should log a warning when cumulative spend exceeds 80 % of the limit.
    /// </summary>
    /// <param name="amount">The amount to record.</param>
    /// <param name="operationName">
    /// A short identifier for the operation that incurred the cost.
    /// Must be sanitized before logging — never logged as raw user input.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    Task RecordSpendAsync(decimal amount, string operationName, CancellationToken ct = default);

    /// <summary>
    /// Returns the total recorded spend for the current calendar month.
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    Task<decimal> GetMonthlySpendAsync(CancellationToken ct = default);
}