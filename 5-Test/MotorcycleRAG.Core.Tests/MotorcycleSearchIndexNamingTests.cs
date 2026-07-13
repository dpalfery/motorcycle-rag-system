using System.Linq;
using MotorcycleRAG.Core.Utilities;
using Xunit;

namespace MotorcycleRAG.Core.Tests;

public class MotorcycleSearchIndexNamingTests
{
    [Theory]
    [InlineData("dirt", "motorcycle-dirt")]
    [InlineData("Touring", "motorcycle-touring")]
    [InlineData(" SPORT ", "motorcycle-sport")]
    [InlineData("CRUISER", "motorcycle-cruiser")]
    public void ForCategory_WithValidCategory_ReturnsCorrectIndexName(string input, string expected)
    {
        var result = MotorcycleSearchIndexNaming.ForCategory(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForCategory_WithNullOrWhitespace_ReturnsDefaultIndexName(string? input)
    {
        var result = MotorcycleSearchIndexNaming.ForCategory(input);
        Assert.Equal("motorcycle-sport", result);
        Assert.Equal(MotorcycleSearchIndexNaming.DefaultIndexName, result);
    }

    [Theory]
    [InlineData("dirt", true)]
    [InlineData("Touring", true)]
    [InlineData(" SPORT ", true)]
    [InlineData("CRUISER", true)]
    [InlineData("invalid", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsValidCategoryWireValue_ChecksValidityCorrectly(string? input, bool expected)
    {
        var result = MotorcycleSearchIndexNaming.IsValidCategoryWireValue(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AllCategoryWireValues_ContainsExactlyFourCategories()
    {
        var categories = MotorcycleSearchIndexNaming.AllCategoryWireValues;
        Assert.Equal(4, categories.Count);
        Assert.Contains("dirt", categories);
        Assert.Contains("touring", categories);
        Assert.Contains("sport", categories);
        Assert.Contains("cruiser", categories);
    }
}
