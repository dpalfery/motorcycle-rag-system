using System.Reflection;
using MotorcycleRAG.Contracts.Models.DTOs.Graph;

namespace MotorcycleRAG.UnitTests.Contracts;

/// <summary>
/// RED-phase contract tests for plan
/// <c>6-Docs/plans/2026-08-01-vector-graph-anchor-id-contract.md</c> (task T4, §4 row T4).
/// Asserts, purely via reflection, that <see cref="GraphNodeDto"/> exposes the two new
/// vector/graph anchor fields — <c>ChunkId</c> and <c>SourceContentHash</c> — required to
/// bridge Azure AI Search chunk records to <c>dbo.GraphNode</c> rows.
///
/// Reflection is used deliberately instead of direct property access
/// (<c>dto.ChunkId = "x"</c>) so that, until task T4's implementation lands the properties on
/// the production DTO, only these specific test methods fail — a direct-access failure would be
/// a compile error that turns the entire test project (and every other RED test in it) red,
/// defeating a gated Red→Green→Refactor pipeline that dispatches tasks independently.
/// </summary>
public sealed class GraphNodeDtoAnchorFieldsTests
{
    [Fact]
    public void GraphNodeDto_HasChunkIdProperty_PublicSettableNullableString()
    {
        var property = typeof(GraphNodeDto).GetProperty("ChunkId");

        property.Should().NotBeNull("GraphNodeDto must expose a public ChunkId anchor property (plan T4)");
        property!.PropertyType.Should().Be<string>("ChunkId must be typed as string (declared string?)");
        property.CanWrite.Should().BeTrue("ChunkId must be a settable property");
        property.GetSetMethod(nonPublic: false).Should().NotBeNull("ChunkId's setter must be public");

        var nullabilityInfo = new NullabilityInfoContext().Create(property);
        nullabilityInfo.WriteState.Should().Be(
            NullabilityState.Nullable,
            "ChunkId must be declared as string? (nullable reference type) per plan T4");
    }

    [Fact]
    public void GraphNodeDto_HasSourceContentHashProperty_PublicSettableNullableString()
    {
        var property = typeof(GraphNodeDto).GetProperty("SourceContentHash");

        property.Should().NotBeNull("GraphNodeDto must expose a public SourceContentHash anchor property (plan T4)");
        property!.PropertyType.Should().Be<string>("SourceContentHash must be typed as string (declared string?)");
        property.CanWrite.Should().BeTrue("SourceContentHash must be a settable property");
        property.GetSetMethod(nonPublic: false).Should().NotBeNull("SourceContentHash's setter must be public");

        var nullabilityInfo = new NullabilityInfoContext().Create(property);
        nullabilityInfo.WriteState.Should().Be(
            NullabilityState.Nullable,
            "SourceContentHash must be declared as string? (nullable reference type) per plan T4");
    }

    [Fact]
    public void GraphNodeDto_ChunkIdAndSourceContentHash_DefaultToNullOnConstruction()
    {
        var chunkIdProperty = typeof(GraphNodeDto).GetProperty("ChunkId");
        var sourceContentHashProperty = typeof(GraphNodeDto).GetProperty("SourceContentHash");

        chunkIdProperty.Should().NotBeNull("ChunkId must exist on GraphNodeDto before its default value can be asserted");
        sourceContentHashProperty.Should().NotBeNull("SourceContentHash must exist on GraphNodeDto before its default value can be asserted");

        var dto = new GraphNodeDto();

        chunkIdProperty!.GetValue(dto).Should().BeNull("ChunkId must default to null on construction");
        sourceContentHashProperty!.GetValue(dto).Should().BeNull("SourceContentHash must default to null on construction");
    }
}
