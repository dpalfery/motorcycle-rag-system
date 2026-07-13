using System.Text.Json;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.UnitTests.Contracts;

/// <summary>
/// Serialization tests for the end-to-end <c>category</c> field (T2).
/// </summary>
/// <remarks>
/// These tests exercise the public contract surface: the
/// <see cref="MotorcycleCategoryJsonConverter"/> bound on the DTOs and the
/// <c>category</c> wire-string round-trip. The private
/// <c>ChunkIndexingService.ChunkIndexRecord</c> deserializes its
/// <c>[JsonPropertyName("category")] string Category</c> using the identical
/// System.Text.Json <c>[JsonPropertyName]</c> contract resolution demonstrated
/// here (a plain string init-property mapped by attribute name under default
/// options), so these tests lock the mechanism the chunk record depends on.
/// </remarks>
public class IngestionJobConfigurationCategorySerializationTests
{
    private static JsonSerializerOptions WebOptions { get; } = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("dirt", nameof(MotorcycleCategory.Dirt))]
    [InlineData("touring", nameof(MotorcycleCategory.Touring))]
    [InlineData("sport", nameof(MotorcycleCategory.Sport))]
    [InlineData("cruiser", nameof(MotorcycleCategory.Cruiser))]
    public void Deserialize_CategoryWireString_MapsToTypedValueObject(string wireValue, string expectedName)
    {
        var json = $$"""
                     {
                       "extractGraphRelationships": true,
                       "ocrEnabled": false,
                       "category": "{{wireValue}}"
                     }
                     """;

        var config = JsonSerializer.Deserialize<IngestionJobConfiguration>(json, WebOptions);

        config.Should().NotBeNull();
        config!.Category.Should().NotBeNull();
        config.Category!.Value.Value.Should().Be(wireValue);
        config.Category!.Value.ToString().Should().Be(wireValue);
        config.Category!.Value.IsDefined.Should().BeTrue();
        expectedName.Should().BeOneOf(
            nameof(MotorcycleCategory.Dirt),
            nameof(MotorcycleCategory.Touring),
            nameof(MotorcycleCategory.Sport),
            nameof(MotorcycleCategory.Cruiser));
    }

    [Fact]
    public void Deserialize_CategoryOmitted_LeavesCategoryNull()
    {
        var json = """{ "extractGraphRelationships": true, "ocrEnabled": true }""";

        var config = JsonSerializer.Deserialize<IngestionJobConfiguration>(json, WebOptions);

        config.Should().NotBeNull();
        config!.Category.Should().BeNull();
    }

    [Theory]
    [InlineData("naked")]
    [InlineData("ADVENTURE")]
    public void Deserialize_InvalidCategory_ThrowsJsonException(string wireValue)
    {
        var json = $$"""{ "category": "{{wireValue}}" }""";

        var act = () => JsonSerializer.Deserialize<IngestionJobConfiguration>(json, WebOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Serialize_TypedValue_WritesCanonicalLowerCaseWireString()
    {
        var config = new IngestionJobConfiguration { Category = MotorcycleCategory.Dirt };

        var json = JsonSerializer.Serialize(config, WebOptions);

        // Canonical wire form is lowercase, matching the index schema field and Python chunkers.
        json.Should().Contain("\"category\":\"dirt\"");
    }

    [Fact]
    public void RoundTrip_PreservesCategory()
    {
        var original = new IngestionJobConfiguration
        {
            ExtractGraphRelationships = false,
            OcrEnabled = true,
            Category = MotorcycleCategory.Touring
        };

        var json = JsonSerializer.Serialize(original, WebOptions);
        var deserialized = JsonSerializer.Deserialize<IngestionJobConfiguration>(json, WebOptions);

        deserialized.Should().NotBeNull();
        deserialized!.Category.Should().Be(MotorcycleCategory.Touring);
    }
}
