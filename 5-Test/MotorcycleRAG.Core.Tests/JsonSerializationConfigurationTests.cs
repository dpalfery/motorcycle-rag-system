using System.Text.Json;
using System.Text.Json.Serialization;
using MotorcycleRAG.Core.Utilities;
using Xunit;

namespace MotorcycleRAG.Core.Tests;

public class JsonSerializationConfigurationTests
{
    [Fact]
    public void DefaultOptions_AreConfiguredCorrectly()
    {
        var options = JsonSerializationConfiguration.DefaultOptions;

        Assert.NotNull(options);
        Assert.False(options.WriteIndented);
        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, options.DefaultIgnoreCondition);
        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Equal(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
        Assert.Contains(options.Converters, c => c is JsonStringEnumConverter);
    }

    [Fact]
    public void PrettyPrintOptions_AreConfiguredCorrectly()
    {
        var options = JsonSerializationConfiguration.PrettyPrintOptions;

        Assert.NotNull(options);
        Assert.True(options.WriteIndented);
        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, options.DefaultIgnoreCondition);
        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Equal(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
    }

    [Fact]
    public void MinimalOptions_AreConfiguredCorrectly()
    {
        var options = JsonSerializationConfiguration.MinimalOptions;

        Assert.NotNull(options);
        Assert.False(options.WriteIndented);
        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, options.DefaultIgnoreCondition);
        Assert.True(options.PropertyNameCaseInsensitive);
        Assert.Equal(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetEnvironmentOptions_ReturnsCorrectOptionsBasedOnEnvironment(bool isDevelopment)
    {
        var options = JsonSerializationConfiguration.GetEnvironmentOptions(isDevelopment);

        if (isDevelopment)
        {
            Assert.True(options.WriteIndented);
            Assert.Same(JsonSerializationConfiguration.PrettyPrintOptions, options);
        }
        else
        {
            Assert.False(options.WriteIndented);
            Assert.Same(JsonSerializationConfiguration.DefaultOptions, options);
        }
    }

    private enum SampleStatus
    {
        PendingReview,
        Approved
    }

    private sealed class SampleRecord
    {
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public SampleStatus Status { get; set; }
    }

    [Fact]
    public void DefaultOptions_Serialize_UsesCamelCasePropertyNamesAndOmitsNullValues()
    {
        var record = new SampleRecord
        {
            FirstName = "Ada",
            MiddleName = null,
            Status = SampleStatus.PendingReview
        };

        var json = JsonSerializer.Serialize(record, JsonSerializationConfiguration.DefaultOptions);

        Assert.Contains("\"firstName\":\"Ada\"", json);
        Assert.DoesNotContain("middleName", json);
        Assert.DoesNotContain("MiddleName", json);
        Assert.DoesNotContain("FirstName", json);
    }

    [Fact]
    public void DefaultOptions_Serialize_WritesEnumAsCamelCaseString()
    {
        var record = new SampleRecord { FirstName = "Grace", Status = SampleStatus.PendingReview };

        var json = JsonSerializer.Serialize(record, JsonSerializationConfiguration.DefaultOptions);

        Assert.Contains("\"status\":\"pendingReview\"", json);
    }

    [Fact]
    public void DefaultOptions_Serialize_WriteIndentedIsFalse_ProducesSingleLineJson()
    {
        var record = new SampleRecord { FirstName = "Linus", Status = SampleStatus.Approved };

        var json = JsonSerializer.Serialize(record, JsonSerializationConfiguration.DefaultOptions);

        Assert.DoesNotContain("\n", json);
    }

    [Fact]
    public void DefaultOptions_Deserialize_IsCaseInsensitiveForPropertyNames()
    {
        const string json = "{\"FIRSTNAME\":\"Marie\",\"STATUS\":\"approved\"}";

        var record = JsonSerializer.Deserialize<SampleRecord>(json, JsonSerializationConfiguration.DefaultOptions);

        Assert.NotNull(record);
        Assert.Equal("Marie", record!.FirstName);
        Assert.Equal(SampleStatus.Approved, record.Status);
    }

    [Fact]
    public void DefaultOptions_Deserialize_ReadsCamelCaseEnumString()
    {
        const string json = "{\"firstName\":\"Rosalind\",\"status\":\"pendingReview\"}";

        var record = JsonSerializer.Deserialize<SampleRecord>(json, JsonSerializationConfiguration.DefaultOptions);

        Assert.NotNull(record);
        Assert.Equal(SampleStatus.PendingReview, record!.Status);
    }

    [Fact]
    public void PrettyPrintOptions_Serialize_ProducesMultiLineIndentedJson()
    {
        var record = new SampleRecord { FirstName = "Katherine", Status = SampleStatus.Approved };

        var json = JsonSerializer.Serialize(record, JsonSerializationConfiguration.PrettyPrintOptions);

        Assert.Contains("\n", json);
        Assert.Contains("\"firstName\": \"Katherine\"", json);
    }

    [Fact]
    public void MinimalOptions_Serialize_ProducesSingleLineJsonWithCamelCaseNames()
    {
        var record = new SampleRecord { FirstName = "Alan", Status = SampleStatus.Approved };

        var json = JsonSerializer.Serialize(record, JsonSerializationConfiguration.MinimalOptions);

        Assert.DoesNotContain("\n", json);
        Assert.Contains("\"firstName\":\"Alan\"", json);
        Assert.Contains("\"status\":\"approved\"", json);
    }

    [Fact]
    public void DefaultOptions_RoundTrip_PreservesValuesThroughSerializeAndDeserialize()
    {
        var original = new SampleRecord
        {
            FirstName = "Hedy",
            MiddleName = "Kiesler",
            Status = SampleStatus.Approved
        };

        var json = JsonSerializer.Serialize(original, JsonSerializationConfiguration.DefaultOptions);
        var roundTripped = JsonSerializer.Deserialize<SampleRecord>(json, JsonSerializationConfiguration.DefaultOptions);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.FirstName, roundTripped!.FirstName);
        Assert.Equal(original.MiddleName, roundTripped.MiddleName);
        Assert.Equal(original.Status, roundTripped.Status);
    }
}
