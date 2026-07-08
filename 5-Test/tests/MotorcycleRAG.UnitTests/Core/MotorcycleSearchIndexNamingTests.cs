using FluentAssertions;
using MotorcycleRAG.Core.Utilities;

namespace MotorcycleRAG.UnitTests.Core;

/// <summary>
/// Unit tests for the canonical category -&gt; index-name mapping (single source of truth
/// shared by the Application-layer labels and the Persistence-layer factory).
/// </summary>
public class MotorcycleSearchIndexNamingTests
{
    private static readonly string[] ExpectedCategoryWireValues = { "dirt", "touring", "sport", "cruiser" };

    [Theory]
    [InlineData("dirt", "motorcycle-dirt")]
    [InlineData("touring", "motorcycle-touring")]
    [InlineData("sport", "motorcycle-sport")]
    [InlineData("cruiser", "motorcycle-cruiser")]
    public void ForCategory_MapsCanonicalWireValueToIndexName(string wireValue, string expectedIndex)
    {
        MotorcycleSearchIndexNaming.ForCategory(wireValue).Should().Be(expectedIndex);
    }

    [Theory]
    [InlineData("Dirt")]
    [InlineData("DIRT")]
    [InlineData("  touring  ")]
    public void ForCategory_IsCaseAndWhitespaceInsensitive(string input)
    {
        var normalized = input.Trim().ToLowerInvariant();
        MotorcycleSearchIndexNaming.ForCategory(input)
            .Should().Be(MotorcycleSearchIndexNaming.IndexNamePrefix + normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForCategory_EmptyOrNull_FallsBackToDefaultIndex(string? input)
    {
        MotorcycleSearchIndexNaming.ForCategory(input).Should().Be(MotorcycleSearchIndexNaming.DefaultIndexName);
    }

    [Fact]
    public void DefaultIndexName_IsSportPartition_MatchingClassifierFallback()
    {
        // R4: the classifier falls back to Sport; the index default must agree so
        // category-unknown routing lands in the same partition.
        MotorcycleSearchIndexNaming.DefaultIndexName.Should().Be("motorcycle-sport");
        MotorcycleSearchIndexNaming.DefaultCategoryWireValue.Should().Be("sport");
    }

    [Theory]
    [InlineData("dirt", true)]
    [InlineData("TOURING", true)]
    [InlineData("naked", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidCategoryWireValue_DetectsTheFourCanonicalValues(string? value, bool expected)
    {
        MotorcycleSearchIndexNaming.IsValidCategoryWireValue(value).Should().Be(expected);
    }

    [Fact]
    public void AllCategoryWireValues_ContainsExactlyTheFourCategories()
    {
        MotorcycleSearchIndexNaming.AllCategoryWireValues
            .Should().BeEquivalentTo(ExpectedCategoryWireValues);
    }
}
