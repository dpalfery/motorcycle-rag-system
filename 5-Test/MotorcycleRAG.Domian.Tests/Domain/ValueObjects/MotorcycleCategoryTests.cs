using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.Domian.Tests.Domain.ValueObjects;

/// <summary>
/// Unit tests for the canonical <see cref="MotorcycleCategory"/> value object.
/// Locks the four valid values and the enforcement contract (T2).
/// </summary>
public class MotorcycleCategoryTests
{
    public static IEnumerable<object[]> ValidCategories =>
    [
        ["dirt", MotorcycleCategory.Dirt],
        ["touring", MotorcycleCategory.Touring],
        ["sport", MotorcycleCategory.Sport],
        ["cruiser", MotorcycleCategory.Cruiser]
    ];

    public static IEnumerable<object[]> InvalidCategories =>
    [
        [""],
        ["  "],
        ["naked"],
        ["ADVENTURE"],
        ["dual-sport"],
        [null!]
    ];

    [Theory]
    [MemberData(nameof(ValidCategories))]
    public void Parse_AcceptsTheFourValidValues_CaseInsensitively(string input, MotorcycleCategory expected)
    {
        var parsed = MotorcycleCategory.Parse(input);

        parsed.Should().Be(expected);
        parsed.Value.Should().Be(input.ToLowerInvariant());
        parsed.ToString().Should().Be(input.ToLowerInvariant());
        parsed.IsDefined.Should().BeTrue();
    }

    [Theory]
    [InlineData("Dirt")]
    [InlineData("DIRT")]
    [InlineData("  Touring  ")]
    [InlineData("Sport")]
    [InlineData("cruiser")]
    public void TryParse_AcceptsAnyCasing(string input)
    {
        var ok = MotorcycleCategory.TryParse(input, out var category);

        ok.Should().BeTrue();
        category.IsDefined.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(InvalidCategories))]
    public void Parse_RejectsInvalidValues(string? input)
    {
        var act = () => MotorcycleCategory.Parse(input);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [MemberData(nameof(InvalidCategories))]
    public void TryParse_ReturnsFalseForInvalidValues(string? input)
    {
        var ok = MotorcycleCategory.TryParse(input, out var category);

        ok.Should().BeFalse();
        category.IsDefined.Should().BeFalse();
        category.Value.Should().BeEmpty();
    }

    [Fact]
    public void All_ContainsExactlyTheFourCanonicalCategories()
    {
        MotorcycleCategory.All.Should().HaveCount(4);
        MotorcycleCategory.All.Should().BeEquivalentTo(new[]
        {
            MotorcycleCategory.Dirt,
            MotorcycleCategory.Touring,
            MotorcycleCategory.Sport,
            MotorcycleCategory.Cruiser
        });
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        MotorcycleCategory.Dirt.Should().Be(MotorcycleCategory.Dirt);
        MotorcycleCategory.Dirt.Should().NotBe(MotorcycleCategory.Sport);
        (MotorcycleCategory.Dirt == MotorcycleCategory.Parse("dirt")).Should().BeTrue();
        (MotorcycleCategory.Dirt != MotorcycleCategory.Sport).Should().BeTrue();
        MotorcycleCategory.Dirt.GetHashCode().Should().Be(MotorcycleCategory.Parse("DIRT").GetHashCode());
    }

    [Fact]
    public void Default_IsEmptyAndNotDefined()
    {
        MotorcycleCategory defaultCategory = default;

        defaultCategory.IsDefined.Should().BeFalse();
        defaultCategory.Value.Should().BeEmpty();
        defaultCategory.ToString().Should().BeEmpty();
        MotorcycleCategory.IsDefinedValue(defaultCategory).Should().BeFalse();
    }

    [Fact]
    public void CanonicalValue_IsLowerCase_ForWireAndFacetingCompatibility()
    {
        // The wire/index value MUST be lowercase to match Azure AI Search faceting
        // conventions and the Python chunker `category` key emission.
        MotorcycleCategory.Dirt.Value.Should().Be("dirt");
        MotorcycleCategory.Touring.Value.Should().Be("touring");
        MotorcycleCategory.Sport.Value.Should().Be("sport");
        MotorcycleCategory.Cruiser.Value.Should().Be("cruiser");
    }
}
