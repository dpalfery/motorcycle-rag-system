using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.Telemetry;

/// <summary>
/// In-memory implementation of <see cref="IBudgetMonitorService"/> that tracks monthly Azure spend
/// and enforces the limit configured in <see cref="FabricIngestionOptions.MonthlyBudgetLimit"/>.
/// Thread-safe: all mutations are serialised through <see cref="_lock"/>.
/// Resets automatically when the calendar month rolls over.
/// </summary>
public sealed class BudgetMonitorService : IBudgetMonitorService
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly FabricIngestionOptions _options;
    private readonly ILogger<BudgetMonitorService> _logger;
    /// <summary>Guards all reads and writes to <see cref="_monthlySpend"/> and <see cref="_trackingMonth"/>.</summary>
    private readonly object _lock = new();

    /// <summary>Accumulated spend for the current tracking month. Mutated only inside <see cref="_lock"/>.</summary>
    private decimal _monthlySpend;

    /// <summary>The month (1-12) for which <see cref="_monthlySpend"/> has been accumulated.</summary>
    private int _trackingMonth;

    /// <summary>The year for which <see cref="_monthlySpend"/> has been accumulated.</summary>
    private int _trackingYear;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises a new instance of <see cref="BudgetMonitorService"/>.
    /// </summary>
    /// <param name="options">Fabric ingestion options (provides <c>MonthlyBudgetLimit</c>).</param>
    /// <param name="logger">Structured logger. Never logs raw user-supplied values.</param>
    public BudgetMonitorService(
        IOptions<FabricIngestionOptions> options,
        ILogger<BudgetMonitorService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;

        var now = DateTime.UtcNow;
        _trackingMonth = now.Month;
        _trackingYear = now.Year;
        _monthlySpend = 0m;
    }

    // -------------------------------------------------------------------------
    // IBudgetMonitorService
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public bool IsWithinBudget(decimal amountToSpend)
    {
        lock (_lock)
        {
            ResetIfMonthRolled();
            return (_monthlySpend + amountToSpend) <= _options.MonthlyBudgetLimit;
        }
    }

    /// <inheritdoc />
    public Task RecordSpendAsync(decimal amount, string operationName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Sanitize operationName before any logging to prevent log injection (ASVS).
        var safeOperationName = SanitizeForLog(operationName);

        decimal newTotal;
        decimal limit;

        lock (_lock)
        {
            ResetIfMonthRolled();
            _monthlySpend += amount;
            newTotal = _monthlySpend;
            limit = _options.MonthlyBudgetLimit;
        }
        _logger.LogDebug(
            "Budget spend recorded. Operation={OperationName} Amount={Amount:F4} MonthlyTotal={MonthlyTotal:F4}",
            safeOperationName,
            amount,
            newTotal);

        // Warn at 80 % threshold; a limit of 0 means enforcement is disabled.
        if (limit > 0m && newTotal >= limit * 0.8m)
        {
            _logger.LogWarning(
                "Monthly budget threshold reached. Operation={OperationName} MonthlyTotal={MonthlyTotal:F4} Limit={Limit:F4} UtilisationPct={UtilisationPct:F1}",
                safeOperationName,
                newTotal,
                limit,
                newTotal / limit * 100m);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<decimal> GetMonthlySpendAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        decimal spend;
        lock (_lock)
        {
            ResetIfMonthRolled();
            spend = _monthlySpend;
        }

        return Task.FromResult(spend);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resets <see cref="_monthlySpend"/> when the calendar month has changed.
    /// Must be called inside <see cref="_lock"/>.
    /// </summary>
    private void ResetIfMonthRolled()
    {
        var now = DateTime.UtcNow;
        if (now.Year == _trackingYear && now.Month == _trackingMonth)
        {
            return;
        }

        _logger.LogInformation(
            "Monthly budget counter reset. PreviousMonth={PreviousYear}-{PreviousMonth:D2} PreviousSpend={PreviousSpend:F4}",
            _trackingYear,
            _trackingMonth,
            _monthlySpend);

        _monthlySpend = 0m;
        _trackingYear = now.Year;
        _trackingMonth = now.Month;
    }

    /// <summary>
    /// Replaces newline and carriage-return characters with a space to prevent log injection.
    /// </summary>
    private static string SanitizeForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace('\n', ' ')
            .Replace('\r', ' ');
    }
}