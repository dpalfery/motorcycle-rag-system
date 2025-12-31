namespace MotorcycleRAG.Contracts.Interfaces;

/// <summary>
/// Interface for correlation service operations
/// </summary>
public interface ICorrelationService
{
    /// <summary>
    /// Gets the current correlation ID
    /// </summary>
    string GetCorrelationId();

    /// <summary>
    /// Sets the correlation ID
    /// </summary>
    void SetCorrelationId(string correlationId);

    /// <summary>
    /// Generates a new correlation ID
    /// </summary>
    string GenerateCorrelationId();

    /// <summary>
    /// Gets or creates a correlation ID
    /// </summary>
    string GetOrCreateCorrelationId();

    /// <summary>
    /// Gets or generates a correlation ID
    /// </summary>
    string GetOrGenerateCorrelationId();

    /// <summary>
    /// Starts a new activity
    /// </summary>
    IDisposable StartActivity(string name);

    /// <summary>
    /// Creates a logging scope
    /// </summary>
    IDisposable CreateLoggingScope();

    /// <summary>
    /// Creates a logging scope with additional properties
    /// </summary>
    IDisposable CreateLoggingScope(Dictionary<string, object> additionalProperties);
}
