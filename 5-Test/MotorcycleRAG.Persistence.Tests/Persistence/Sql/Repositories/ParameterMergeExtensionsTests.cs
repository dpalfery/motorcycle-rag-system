using Dapper;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class ParameterMergeExtensionsTests
{
    [Fact]
    public void Merge_ShouldReturnFirstObjectUnchanged_WhenSecondIsNull()
    {
        var first = new { Name = "John", Age = 30 };

        var result = first.Merge(null);

        // When second is null, the method returns first directly (not DynamicParameters)
        result.Should().BeSameAs(first);
    }

    [Fact]
    public void Merge_ShouldReturnFirstObject_WhenSecondIsNull()
    {
        var first = new { Id = 42, Name = "test" };

        var result = first.Merge(null);

        // Returns the original object, not wrapped
        result.Should().BeSameAs(first);
    }

    [Fact]
    public void Merge_ShouldOverrideFirstObjectProperties_WithSecondObjectValues()
    {
        var first = new { Name = "John", Age = 30, City = "NYC" };
        var second = new { Age = 31, City = "Boston" };

        var result = first.Merge(second);

        var dict = ToDictionary((DynamicParameters)result);
        dict.Should().HaveCount(3);
        dict["Name"].Should().Be("John");   // from first, not overridden
        dict["Age"].Should().Be(31);        // overridden by second
        dict["City"].Should().Be("Boston"); // overridden by second
    }

    [Fact]
    public void Merge_ShouldAddNewProperties_FromSecondObject()
    {
        var first = new { Id = 1 };
        var second = new { Name = "added", Active = true };

        var result = first.Merge(second);

        var dict = ToDictionary((DynamicParameters)result);
        dict.Should().HaveCount(3);
        dict["Id"].Should().Be(1);
        dict["Name"].Should().Be("added");
        dict["Active"].Should().Be(true);
    }

    [Fact]
    public void Merge_ShouldHandleNullValues_InSecondObject()
    {
        var first = new { Name = "John", Age = 30 };
        var second = new { Age = (object?)null, Name = (object?)null };

        var result = first.Merge(second);

        var dict = ToDictionary((DynamicParameters)result);
        dict["Name"].Should().BeNull();
        dict["Age"].Should().BeNull();
    }

    [Fact]
    public void Merge_ShouldReturnSameType_AsFirstArgument()
    {
        var first = new { A = 1 };
        var second = new { B = "two" };

        var result = first.Merge(second);

        result.Should().BeOfType<DynamicParameters>();
    }

    [Fact]
    public void Merge_ShouldHandleSecondObjectWithNoProperties()
    {
        var first = new { X = 10, Y = 20 };

        var result = first.Merge(new { });

        var dict = ToDictionary((DynamicParameters)result);
        dict.Should().HaveCount(2);
        dict["X"].Should().Be(10);
        dict["Y"].Should().Be(20);
    }

    [Fact]
    public void Merge_ShouldUseSecondObjectProperties_WhenConflicting()
    {
        var first = new { Version = "1.0", Enabled = false, Count = 5 };
        var second = new { Version = "2.0", Enabled = true };

        var result = first.Merge(second);

        var dict = ToDictionary((DynamicParameters)result);
        dict["Version"].Should().Be("2.0");
        dict["Enabled"].Should().Be(true);
        dict["Count"].Should().Be(5); // unchanged, only in first
    }

    private static Dictionary<string, object?> ToDictionary(DynamicParameters parameters)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        // DynamicParameters exposes parameter names via the ParameterNames property
        foreach (var name in parameters.ParameterNames)
        {
            dict[name] = parameters.Get<object?>(name);
        }

        return dict;
    }
}
