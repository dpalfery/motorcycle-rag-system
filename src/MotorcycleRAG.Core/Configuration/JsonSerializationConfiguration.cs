using System.Text.Json;
using System.Text.Json.Serialization;

namespace MotorcycleRAG.Core.Configuration;

/// <summary>
/// JSON serialization configuration for API responses
/// </summary>
public static class JsonSerializationConfiguration
{
    /// <summary>
    /// Gets the default JSON serializer options for the application
    /// </summary>
    public static JsonSerializerOptions DefaultOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new DateTimeConverter(),
            new TimeSpanConverter(),
            new FloatArrayConverter()
        }
    };

    /// <summary>
    /// Gets JSON serializer options for pretty-printed output (development/debugging)
    /// </summary>
    public static JsonSerializerOptions PrettyPrintOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new DateTimeConverter(),
            new TimeSpanConverter(),
            new FloatArrayConverter()
        }
    };

    /// <summary>
    /// Gets JSON serializer options for minimal output (production)
    /// </summary>
    public static JsonSerializerOptions MinimalOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new DateTimeConverter(),
            new TimeSpanConverter()
            // Note: FloatArrayConverter excluded for minimal output to reduce size
        }
    };
}

/// <summary>
/// Custom DateTime converter for consistent ISO 8601 formatting
/// </summary>
public class DateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dateString = reader.GetString()!;
        return DateTime.Parse(dateString, null, System.Globalization.DateTimeStyles.RoundtripKind);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
    }
}

/// <summary>
/// Custom TimeSpan converter for readable duration formatting
/// </summary>
public class TimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return TimeSpan.Parse(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(@"hh\:mm\:ss\.fff"));
    }
}

/// <summary>
/// Custom converter for float arrays (vector embeddings) with truncation for readability
/// </summary>
public class FloatArrayConverter : JsonConverter<float[]>
{
    private const int MaxDisplayElements = 5;

    public override float[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        var list = new List<float>();
        
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("Expected start of array");

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
                break;

            if (reader.TokenType == JsonTokenType.Number)
            {
                list.Add(reader.GetSingle());
            }
        }

        return list.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, float[] value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();

        // For large arrays (like embeddings), only show first few elements for readability
        if (value.Length > MaxDisplayElements)
        {
            for (int i = 0; i < MaxDisplayElements; i++)
            {
                writer.WriteNumberValue(value[i]);
            }
            
            // Add a string indicator that the array is truncated
            writer.WriteStringValue($"... ({value.Length - MaxDisplayElements} more elements)");
        }
        else
        {
            foreach (var item in value)
            {
                writer.WriteNumberValue(item);
            }
        }

        writer.WriteEndArray();
    }
}

/// <summary>
/// Extension methods for configuring JSON serialization
/// </summary>
public static class JsonConfigurationExtensions
{
    /// <summary>
    /// Gets JSON serializer options based on environment
    /// </summary>
    /// <param name="isDevelopment">Whether the application is running in development mode</param>
    /// <returns>Configured JsonSerializerOptions</returns>
    public static JsonSerializerOptions GetEnvironmentOptions(bool isDevelopment = false)
    {
        return isDevelopment ? JsonSerializationConfiguration.PrettyPrintOptions : JsonSerializationConfiguration.DefaultOptions;
    }
}