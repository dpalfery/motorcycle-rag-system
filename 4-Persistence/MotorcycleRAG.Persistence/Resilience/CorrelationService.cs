using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Utilities;
using System.Collections;
using System.Diagnostics;


namespace MotorcycleRAG.Persistence.Resilience;

/// <summary>
/// Service for managing correlation IDs throughout the request lifecycle
/// </summary>
public class CorrelationService : ICorrelationService
{
    private readonly ILogger<CorrelationService> _logger;
    private static readonly AsyncLocal<string?> _correlationId = new();

    public CorrelationService(ILogger<CorrelationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string GetCorrelationId()
    {
        return GetOrCreateCorrelationId();
    }

    /// <inheritdoc />
    public string GenerateCorrelationId()
    {
        // Use a format similar to W3C Trace Context but simplified
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        var random = Guid.NewGuid().ToString("N")[..12]; // Take first 12 chars
        return $"corr-{timestamp}-{random}";
    }

    /// <summary>
    /// Gets the current correlation ID or generates a new one
    /// </summary>
    public string GetOrCreateCorrelationId()
    {
        var currentId = _correlationId.Value;
        if (!string.IsNullOrEmpty(currentId))
        {
            return currentId;
        }

        // Try to get from Activity (OpenTelemetry/Application Insights)
        var activity = Activity.Current;
        if (activity?.Id != null)
        {
            _correlationId.Value = activity.Id;
            return activity.Id;
        }

        // Generate new correlation ID
        var newId = GenerateCorrelationId();
        _correlationId.Value = newId;

        _logger.LogDebug("Generated new correlation ID: {CorrelationId}", LogSanitizer.Sanitize(newId));
        return newId;
    }

    /// <inheritdoc />
    public string GetOrGenerateCorrelationId() => GetOrCreateCorrelationId();

    /// <inheritdoc />
    public IDisposable StartActivity(string name)
    {
        var activity = new Activity(name);
        activity.Start();
        return activity;
    }

    /// <summary>
    /// Sets the correlation ID for the current context
    /// </summary>
    public void SetCorrelationId(string correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("Correlation ID cannot be null or empty", nameof(correlationId));
        }

        var sanitizedId = LogSanitizer.Sanitize(correlationId);
        _correlationId.Value = sanitizedId;
        _logger.LogDebug("Set correlation ID: {CorrelationId}", sanitizedId);
    }

    /// <summary>
    /// Clears the current correlation ID
    /// </summary>
    public void ClearCorrelationId()
    {
        var currentId = _correlationId.Value;
        _correlationId.Value = null;

        if (!string.IsNullOrEmpty(currentId))
        {
            _logger.LogDebug("Cleared correlation ID: {CorrelationId}", LogSanitizer.Sanitize(currentId));
        }
    }

    /// <summary>
    /// Executes an operation with a specific correlation ID
    /// </summary>
    public async Task<T> ExecuteWithCorrelationAsync<T>(
        string correlationId,
        Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var previousId = _correlationId.Value;
        SetCorrelationId(correlationId);

        try
        {
            return await operation();
        }
        finally
        {
            _correlationId.Value = previousId;
        }
    }

    /// <summary>
    /// Executes an operation with a specific correlation ID (no return value)
    /// </summary>
    public async Task ExecuteWithCorrelationAsync(
        string correlationId,
        Func<Task> operation)
    {
        await ExecuteWithCorrelationAsync(correlationId, async () =>
        {
            await operation();
            return true; // Dummy return value
        });
    }

    /// <summary>
    /// Creates a logging scope with the current correlation ID
    /// </summary>
    public IDisposable CreateLoggingScope()
    {
        var correlationId = GetOrCreateCorrelationId();
        return _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = LogSanitizer.Sanitize(correlationId)
        })!;
    }

    /// <summary>
    /// Creates a logging scope with additional properties
    /// </summary>
    public IDisposable CreateLoggingScope(Dictionary<string, object> additionalProperties)
    {
        var correlationId = GetOrCreateCorrelationId();
        ArgumentNullException.ThrowIfNull(additionalProperties);

        var scopeProperties = SanitizeScopeProperties(additionalProperties);
        scopeProperties["CorrelationId"] = LogSanitizer.Sanitize(correlationId);

        return _logger.BeginScope(scopeProperties)!;
    }

    private static Dictionary<string, object> SanitizeScopeProperties(
        IEnumerable<KeyValuePair<string, object>> properties)
    {
        var sanitizedProperties = new Dictionary<string, object>();
        var sanitizedValues = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);

        foreach (var (key, value) in properties)
        {
            sanitizedProperties[LogSanitizer.Sanitize(key)] = SanitizeScopeValue(value, sanitizedValues)!;
        }

        return sanitizedProperties;
    }

    internal static object? SanitizeScopeValue(
        object? value,
        IDictionary<object, object> sanitizedValues)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string stringValue)
        {
            return LogSanitizer.Sanitize(stringValue);
        }

        var valueType = value.GetType();
        if (valueType.IsValueType)
        {
            return SanitizeControlBearingRenderedValue(value);
        }

        if (sanitizedValues.TryGetValue(value, out var sanitizedValue))
        {
            return sanitizedValue;
        }

        var dictionaryInterface = FindGenericInterface(valueType, typeof(IDictionary<,>))
            ?? FindGenericInterface(valueType, typeof(IReadOnlyDictionary<,>));
        if (dictionaryInterface is not null && value is IEnumerable dictionaryEntries)
        {
            return SanitizeDictionary(
                dictionaryEntries,
                valueType,
                dictionaryInterface,
                sanitizedValues);
        }

        if (value is Array array)
        {
            return SanitizeArray(array, sanitizedValues);
        }

        var enumerableInterface = FindGenericInterface(valueType, typeof(IEnumerable<>));
        if (enumerableInterface is not null && value is IEnumerable values)
        {
            return SanitizeEnumerable(
                values,
                valueType,
                enumerableInterface,
                sanitizedValues);
        }

        return SanitizeControlBearingRenderedValue(value);
    }

    private static object SanitizeDictionary(
        IEnumerable entries,
        Type sourceType,
        Type dictionaryInterface,
        IDictionary<object, object> sanitizedValues)
    {
        var typeArguments = dictionaryInterface.GetGenericArguments();
        var sanitizedDictionary = CreateDictionary(sourceType, typeArguments);
        sanitizedValues.Add(entries, sanitizedDictionary);

        var add = FindGenericInterface(sanitizedDictionary.GetType(), typeof(IDictionary<,>))
            ?.GetMethod(nameof(IDictionary<object, object>.Add), typeArguments);
        if (add is null)
        {
            throw new InvalidOperationException($"Unable to create a writable copy of {sourceType.FullName}.");
        }

        foreach (var entry in entries)
        {
            var entryType = entry.GetType();
            var key = entryType.GetProperty("Key")!.GetValue(entry);
            var entryValue = entryType.GetProperty("Value")!.GetValue(entry);
            var sanitizedEntryValue = SanitizeScopeValue(entryValue, sanitizedValues);

            add.Invoke(sanitizedDictionary, [SanitizeScopeKey(key), sanitizedEntryValue]);
        }

        return sanitizedDictionary;
    }

    private static object? SanitizeScopeKey(object? key)
    {
        return key is string stringKey
            ? LogSanitizer.Sanitize(stringKey)
            : key;
    }

    private static object SanitizeControlBearingRenderedValue(object value)
    {
        var renderedValue = value.ToString();
        return renderedValue is not null && renderedValue.Any(char.IsControl)
            ? LogSanitizer.Sanitize(renderedValue)
            : value;
    }

    private static object SanitizeArray(
        Array values,
        IDictionary<object, object> sanitizedValues)
    {
        var elementType = values.GetType().GetElementType()!;
        var lengths = Enumerable.Range(0, values.Rank).Select(values.GetLength).ToArray();
        var lowerBounds = Enumerable.Range(0, values.Rank).Select(values.GetLowerBound).ToArray();
        var sanitizedArray = Array.CreateInstance(elementType, lengths, lowerBounds);
        sanitizedValues.Add(values, sanitizedArray);

        var indices = new int[values.Rank];
        CopySanitizedArrayValues(values, sanitizedArray, indices, 0, sanitizedValues);
        return sanitizedArray;
    }

    private static void CopySanitizedArrayValues(
        Array source,
        Array destination,
        int[] indices,
        int dimension,
        IDictionary<object, object> sanitizedValues)
    {
        var lowerBound = source.GetLowerBound(dimension);
        var upperBound = source.GetUpperBound(dimension);

        for (var index = lowerBound; index <= upperBound; index++)
        {
            indices[dimension] = index;
            if (dimension == source.Rank - 1)
            {
                destination.SetValue(SanitizeScopeValue(source.GetValue(indices), sanitizedValues), indices);
            }
            else
            {
                CopySanitizedArrayValues(source, destination, indices, dimension + 1, sanitizedValues);
            }
        }
    }

    private static object SanitizeEnumerable(
        IEnumerable values,
        Type sourceType,
        Type enumerableInterface,
        IDictionary<object, object> sanitizedValues)
    {
        var elementType = enumerableInterface.GetGenericArguments()[0];
        var sanitizedCollection = CreateCollection(sourceType, elementType);
        sanitizedValues.Add(values, sanitizedCollection);

        var collectionInterface = FindGenericInterface(sanitizedCollection.GetType(), typeof(ICollection<>));
        var add = collectionInterface?.GetMethod(nameof(ICollection<object>.Add), [elementType]);
        if (add is null)
        {
            throw new InvalidOperationException($"Unable to create a writable copy of {sourceType.FullName}.");
        }

        foreach (var item in values)
        {
            add.Invoke(sanitizedCollection, [SanitizeScopeValue(item, sanitizedValues)]);
        }

        return sanitizedCollection;
    }

    private static Type? FindGenericInterface(Type type, Type genericInterface)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == genericInterface)
        {
            return type;
        }

        return type.GetInterfaces().FirstOrDefault(candidate =>
            candidate.IsGenericType && candidate.GetGenericTypeDefinition() == genericInterface);
    }

    private static object CreateDictionary(Type sourceType, Type[] typeArguments)
    {
        if (!sourceType.IsAbstract &&
            sourceType.GetConstructor(Type.EmptyTypes) is not null &&
            FindGenericInterface(sourceType, typeof(ICollection<>)) is not null)
        {
            return Activator.CreateInstance(sourceType)!;
        }

        return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeArguments))!;
    }

    private static object CreateCollection(Type sourceType, Type elementType)
    {
        if (!sourceType.IsAbstract &&
            sourceType.GetConstructor(Type.EmptyTypes) is not null &&
            FindGenericInterface(sourceType, typeof(ICollection<>)) is not null)
        {
            return Activator.CreateInstance(sourceType)!;
        }

        return Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
    }

}

/// <summary>
/// Extension methods for ILogger to automatically include correlation ID
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "Extension methods forward the template")]
public static class LoggerExtensions
{
    /// <summary>
    /// Logs an error with correlation context
    /// </summary>
    public static void LogErrorWithCorrelation<T>(
        this ILogger<T> logger,
        Exception exception,
        string message,
        string correlationId,
        params object[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(args);

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = LogSanitizer.Sanitize(correlationId)
        });

        var formattedArgs = new object[args.Length];
        args.CopyTo(formattedArgs, 0);
        SanitizeStringArguments(formattedArgs);
        logger.LogError(exception, LogSanitizer.Sanitize(message), formattedArgs);
    }

    /// <summary>
    /// Logs a warning with correlation context
    /// </summary>
    public static void LogWarningWithCorrelation<T>(
        this ILogger<T> logger,
        string message,
        string correlationId,
        params object[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(args);

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = LogSanitizer.Sanitize(correlationId)
        });

        var formattedArgs = new object[args.Length];
        args.CopyTo(formattedArgs, 0);
        SanitizeStringArguments(formattedArgs);
        logger.LogWarning(LogSanitizer.Sanitize(message), formattedArgs);
    }

    /// <summary>
    /// Logs information with correlation context
    /// </summary>
    public static void LogInformationWithCorrelation<T>(
        this ILogger<T> logger,
        string message,
        string correlationId,
        params object[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(args);

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = LogSanitizer.Sanitize(correlationId)
        });

        var formattedArgs = new object[args.Length];
        args.CopyTo(formattedArgs, 0);
        SanitizeStringArguments(formattedArgs);
        logger.LogInformation(LogSanitizer.Sanitize(message), formattedArgs);
    }

    private static void SanitizeStringArguments(object[] arguments)
    {
        var sanitizedValues = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);

        for (var index = 0; index < arguments.Length; index++)
        {
            arguments[index] = CorrelationService.SanitizeScopeValue(arguments[index], sanitizedValues)!;
        }
    }
}
