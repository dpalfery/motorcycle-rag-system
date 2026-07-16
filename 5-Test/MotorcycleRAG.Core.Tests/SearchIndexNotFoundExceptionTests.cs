using System;
using MotorcycleRAG.Core.Exceptions;
using Xunit;

namespace MotorcycleRAG.Core.Tests;

public class SearchIndexNotFoundExceptionTests
{
    [Fact]
    public void Constructor_SetsIndexName_AndMessage()
    {
        var exception = new SearchIndexNotFoundException("my-index");

        Assert.Equal("my-index", exception.IndexName);
        Assert.Contains("my-index", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Constructor_WithNullIndexName_SetsEmptyIndexName()
    {
        var exception = new SearchIndexNotFoundException(null!);

        Assert.Equal(string.Empty, exception.IndexName);
        Assert.Contains("<null>", exception.Message);
    }

    [Fact]
    public void Constructor_WithInnerException_SetsInnerException()
    {
        var inner = new Exception("inner");
        var exception = new SearchIndexNotFoundException("my-index", inner);

        Assert.Equal("my-index", exception.IndexName);
        Assert.Equal(inner, exception.InnerException);
    }

    [Fact]
    public void ParameterlessConstructor_SetsEmptyIndexName()
    {
        var exception = new SearchIndexNotFoundException();

        Assert.Equal(string.Empty, exception.IndexName);
        Assert.Contains("''", exception.Message);
        Assert.Null(exception.InnerException);
    }
}
