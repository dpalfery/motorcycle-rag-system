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
}
