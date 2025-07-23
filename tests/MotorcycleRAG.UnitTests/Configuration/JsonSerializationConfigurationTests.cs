using System.Text.Json;
using MotorcycleRAG.Core.Configuration;
using MotorcycleRAG.Core.Models;

namespace MotorcycleRAG.UnitTests.Configuration;

public class JsonSerializationConfigurationTests
{
    [Fact]
    public void DefaultOptions_ShouldHaveCamelCaseNaming()
    {
        // Arrange & Act
        var options = JsonSerializationConfiguration.DefaultOptions;

        // Assert
        options.PropertyNamingPolicy.Should().Be(JsonNamingPolicy.CamelCase);
        options.PropertyNameCaseInsensitive.Should().BeTrue();
    }

    [Fact]
    public void DefaultOptions_ShouldIgnoreNullValues()
    {
        // Arrange & Act
        var options = JsonSerializationConfiguration.DefaultOptions;

        // Assert
        options.DefaultIgnoreCondition.Should().Be(System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull);
    }

    [Fact]
    public void PrettyPrintOptions_ShouldBeIndented()
    {
        // Arrange & Act
        var options = JsonSerializationConfiguration.PrettyPrintOptions;

        // Assert
        options.WriteIndented.Should().BeTrue();
    }

    [Fact]
    public void MinimalOptions_ShouldNotBeIndented()
    {
        // Arrange & Act
        var options = JsonSerializationConfiguration.MinimalOptions;

        // Assert
        options.WriteIndented.Should().BeFalse();
    }

    [Fact]
    public void SerializeMotorcycleSpecification_ShouldUseCamelCase()
    {
        // Arrange
        var specification = new MotorcycleSpecification
        {
            Id = "test-123",
            Make = "Honda",
            Model = "CBR1000RR",
            Year = 2023
        };

        // Act
        var json = JsonSerializer.Serialize(specification, JsonSerializationConfiguration.DefaultOptions);

        // Assert
        json.Should().Contain("\"id\":");
        json.Should().Contain("\"make\":");
        json.Should().Contain("\"model\":");
        json.Should().Contain("\"year\":");
    }

    [Fact]
    public void SerializeMotorcycleSpecification_ShouldIgnoreNullValues()
    {
        // Arrange
        var specification = new MotorcycleSpecification
        {
            Id = "test-123",
            Make = "Honda",
            Model = "CBR1000RR",
            Year = 2023,
            Engine = null // This should be ignored
        };

        // Act
        var json = JsonSerializer.Serialize(specification, JsonSerializationConfiguration.DefaultOptions);

        // Assert
        json.Should().NotContain("\"engine\":");
    }

    [Fact]
    public void SerializeDocumentType_ShouldUseCamelCaseEnum()
    {
        // Arrange
        var document = new MotorcycleDocument
        {
            Id = "doc-123",
            Title = "Test Document",
            Content = "Test content for document",
            Type = DocumentType.TechnicalDocument
        };

        // Act
        var json = JsonSerializer.Serialize(document, JsonSerializationConfiguration.DefaultOptions);

        // Assert
        json.Should().Contain("\"technicalDocument\"");
    }

    [Fact]
    public void SerializeSearchAgentType_ShouldUseCamelCaseEnum()
    {
        // Arrange
        var searchResult = new SearchResult
        {
            Id = "result-123",
            Content = "Test content",
            RelevanceScore = 0.85f,
            Source = new SearchSource
            {
                AgentType = SearchAgentType.VectorSearch,
                SourceName = "Test Source"
            }
        };

        // Act
        var json = JsonSerializer.Serialize(searchResult, JsonSerializationConfiguration.DefaultOptions);

        // Assert
        json.Should().Contain("\"vectorSearch\"");
    }

    [Fact]
    public void DeserializeMotorcycleSpecification_ShouldBeCaseInsensitive()
    {
        // Arrange
        var json = """
        {
            "ID": "test-123",
            "MAKE": "Honda",
            "MODEL": "CBR1000RR",
            "YEAR": 2023
        }
        """;

        // Act
        var specification = JsonSerializer.Deserialize<MotorcycleSpecification>(json, JsonSerializationConfiguration.DefaultOptions);

        // Assert
        specification.Should().NotBeNull();
        specification!.Id.Should().Be("test-123");
        specification.Make.Should().Be("Honda");
        specification.Model.Should().Be("CBR1000RR");
        specification.Year.Should().Be(2023);
    }

    [Fact]
    public void SerializeQueryResponse_ShouldIncludeAllProperties()
    {
        // Arrange
        var response = new MotorcycleQueryResponse
        {
            Response = "Test response",
            QueryId = "query-123",
            Sources = new[]
            {
                new SearchResult
                {
                    Id = "result-1",
                    Content = "Test content",
                    RelevanceScore = 0.9f,
                    Source = new SearchSource
                    {
                        AgentType = SearchAgentType.VectorSearch,
                        SourceName = "Vector DB"
                    }
                }
            },
            Metrics = new QueryMetrics
            {
                TotalDuration = TimeSpan.FromMilliseconds(500),
                TokensUsed = 150,
                EstimatedCost = 0.001m,
                ResultsFound = 1
            }
        };

        // Act
        var json = JsonSerializer.Serialize(response, JsonSerializationConfiguration.DefaultOptions);

        // Assert
        json.Should().Contain("\"response\":");
        json.Should().Contain("\"queryId\":");
        json.Should().Contain("\"sources\":");
        json.Should().Contain("\"metrics\":");
        json.Should().Contain("\"totalDuration\":");
        json.Should().Contain("\"tokensUsed\":");
        json.Should().Contain("\"estimatedCost\":");
        json.Should().Contain("\"resultsFound\":");
    }
}

public class DateTimeConverterTests
{
    private readonly DateTimeConverter _converter;

    public DateTimeConverterTests()
    {
        _converter = new DateTimeConverter();
    }

    [Fact]
    public void Write_ShouldFormatDateTimeAsISO8601()
    {
        // Arrange
        var dateTime = new DateTime(2023, 12, 25, 14, 30, 45, 123, DateTimeKind.Utc);
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        _converter.Write(writer, dateTime, JsonSerializationConfiguration.DefaultOptions);
        writer.Flush();

        // Assert
        var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("\"2023-12-25T14:30:45.123Z\"");
    }

    [Fact]
    public void Read_ShouldParseISO8601DateTime()
    {
        // Arrange
        var json = "\"2023-12-25T14:30:45.123Z\"";
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
        reader.Read(); // Move to the string token

        // Act
        var result = _converter.Read(ref reader, typeof(DateTime), JsonSerializationConfiguration.DefaultOptions);

        // Assert
        result.Year.Should().Be(2023);
        result.Month.Should().Be(12);
        result.Day.Should().Be(25);
        result.Hour.Should().Be(14);
        result.Minute.Should().Be(30);
        result.Second.Should().Be(45);
        result.Millisecond.Should().Be(123);
    }
}

public class TimeSpanConverterTests
{
    private readonly TimeSpanConverter _converter;

    public TimeSpanConverterTests()
    {
        _converter = new TimeSpanConverter();
    }

    [Fact]
    public void Write_ShouldFormatTimeSpanAsReadableString()
    {
        // Arrange
        var timeSpan = new TimeSpan(0, 1, 30, 45, 123); // 1 hour, 30 minutes, 45 seconds, 123 milliseconds
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        _converter.Write(writer, timeSpan, JsonSerializationConfiguration.DefaultOptions);
        writer.Flush();

        // Assert
        var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("\"01:30:45.123\"");
    }

    [Fact]
    public void Read_ShouldParseTimeSpanString()
    {
        // Arrange
        var json = "\"01:30:45.123\"";
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
        reader.Read(); // Move to the string token

        // Act
        var result = _converter.Read(ref reader, typeof(TimeSpan), JsonSerializationConfiguration.DefaultOptions);

        // Assert
        result.Hours.Should().Be(1);
        result.Minutes.Should().Be(30);
        result.Seconds.Should().Be(45);
        result.Milliseconds.Should().Be(123);
    }
}

public class FloatArrayConverterTests
{
    private readonly FloatArrayConverter _converter;

    public FloatArrayConverterTests()
    {
        _converter = new FloatArrayConverter();
    }

    [Fact]
    public void Write_WithSmallArray_ShouldSerializeAllElements()
    {
        // Arrange
        var array = new float[] { 1.0f, 2.0f, 3.0f };
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        _converter.Write(writer, array, JsonSerializationConfiguration.DefaultOptions);
        writer.Flush();

        // Assert
        var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("[1,2,3]");
    }

    [Fact]
    public void Write_WithLargeArray_ShouldTruncateElements()
    {
        // Arrange
        var array = new float[] { 1.0f, 2.0f, 3.0f, 4.0f, 5.0f, 6.0f, 7.0f, 8.0f };
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        _converter.Write(writer, array, JsonSerializationConfiguration.DefaultOptions);
        writer.Flush();

        // Assert
        var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Contain("[1,2,3,4,5,");
        json.Should().Contain("3 more elements");
    }

    [Fact]
    public void Write_WithNullArray_ShouldSerializeNull()
    {
        // Arrange
        float[]? array = null;
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        // Act
        _converter.Write(writer, array!, JsonSerializationConfiguration.DefaultOptions);
        writer.Flush();

        // Assert
        var json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        json.Should().Be("null");
    }

    [Fact]
    public void Read_ShouldDeserializeFloatArray()
    {
        // Arrange
        var json = "[1.0, 2.0, 3.0]";
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
        reader.Read(); // Move to the start array token

        // Act
        var result = _converter.Read(ref reader, typeof(float[]), JsonSerializationConfiguration.DefaultOptions);

        // Assert
        result.Should().NotBeNull();
        result!.Should().HaveCount(3);
        result![0].Should().Be(1.0f);
        result[1].Should().Be(2.0f);
        result[2].Should().Be(3.0f);
    }

    [Fact]
    public void Read_WithNullValue_ShouldReturnNull()
    {
        // Arrange
        var json = "null";
        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(json));
        reader.Read(); // Move to the null token

        // Act
        var result = _converter.Read(ref reader, typeof(float[]), JsonSerializationConfiguration.DefaultOptions);

        // Assert
        result.Should().BeNull();
    }
}