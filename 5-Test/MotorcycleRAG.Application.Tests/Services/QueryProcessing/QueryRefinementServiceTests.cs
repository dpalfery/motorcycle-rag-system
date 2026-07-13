using System;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services.QueryProcessing;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.QueryProcessing;

public sealed class QueryRefinementServiceTests
{
    private readonly QueryRefinementService _sut = new(NullLogger<QueryRefinementService>.Instance);

    // -------------------------------------------------------------------
    // Constructor guard
    // -------------------------------------------------------------------

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new QueryRefinementService(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // -------------------------------------------------------------------
    // Method guard
    // -------------------------------------------------------------------

    [Fact]
    public void GenerateNoResultsResponse_NullQuery_ThrowsArgumentNullException()
    {
        var act = () => _sut.GenerateNoResultsResponse(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // -------------------------------------------------------------------
    // Generic short query: hits all four suggestion branches
    // -------------------------------------------------------------------

    [Fact]
    public void GenerateNoResultsResponse_ShortGenericQuery_ContainsAllSuggestions()
    {
        const string query = "abc";

        var result = _sut.GenerateNoResultsResponse(query);

        result.Should().Contain("Be more specific");
        result.Should().Contain("Add context");
        result.Should().Contain("Specify brands/models");
        result.Should().Contain("Include year");
    }

    [Fact]
    public void GenerateNoResultsResponse_ShortGenericQuery_ContainsGenericExampleQueries()
    {
        const string query = "abc";

        var result = _sut.GenerateNoResultsResponse(query);

        result.Should().Contain("Honda CBR1000RR 2023 specifications");
        result.Should().Contain("Yamaha YZF-R1 vs Kawasaki Ninja ZX-10R");
    }

    [Fact]
    public void GenerateNoResultsResponse_ShortGenericQuery_IncludesHeaderAndOriginalQuery()
    {
        const string query = "abc";

        var result = _sut.GenerateNoResultsResponse(query);

        result.Should().Contain("# No Results Found");
        result.Should().Contain("I couldn't find information for: \"abc\"");
    }

    // -------------------------------------------------------------------
    // Query naming a known brand: subject-specific examples, no brand suggestion
    // -------------------------------------------------------------------

    [Fact]
    public void GenerateNoResultsResponse_QueryWithBrandNameAndYearAndTerm_OmitsSuggestionsAndUsesBrandSubject()
    {
        const string query = "Honda CBR1000RR 2023 specifications";

        var result = _sut.GenerateNoResultsResponse(query);

        result.Should().NotContain("Be more specific");
        result.Should().NotContain("Add context");
        result.Should().NotContain("Specify brands/models");
        result.Should().NotContain("Include year");
        result.Should().Contain("Honda specifications and performance");
        result.Should().Contain("Honda maintenance schedule");
    }

    // -------------------------------------------------------------------
    // Query naming only a model indicator (no brand): ExtractMainSubject falls
    // back to the model indicator.
    // -------------------------------------------------------------------

    [Fact]
    public void GenerateNoResultsResponse_QueryWithModelIndicatorOnly_ExtractsModelIndicatorAsSubject()
    {
        const string query = "CBR1000RR 2023 specs and pricing details";

        var result = _sut.GenerateNoResultsResponse(query);

        result.Should().NotContain("Specify brands/models");
        result.Should().Contain("CBR specifications and performance");
        result.Should().Contain("CBR maintenance schedule");
    }

    // -------------------------------------------------------------------
    // Missing motorcycle terms suggestion
    // -------------------------------------------------------------------

    [Fact]
    public void GenerateNoResultsResponse_QueryWithoutMotorcycleTerms_IncludesAddContextSuggestion()
    {
        const string query = "Honda CBR1000RR 2023 pricing details here";

        var result = _sut.GenerateNoResultsResponse(query);

        result.Should().Contain("Add context");
    }

    [Fact]
    public void GenerateNoResultsResponse_QueryWithMotorcycleTerm_OmitsAddContextSuggestion()
    {
        const string query = "Honda CBR1000RR 2023 specifications";

        var result = _sut.GenerateNoResultsResponse(query);

        result.Should().NotContain("Add context");
    }

    // -------------------------------------------------------------------
    // Missing year suggestion
    // -------------------------------------------------------------------

    [Fact]
    public void GenerateNoResultsResponse_QueryWithoutYear_IncludesYearSuggestion()
    {
        // Deliberately avoids any embedded 4-digit run (e.g. "CBR1000RR" contains
        // "1000", which the production regex \d{4} treats as a year) so this
        // query genuinely has no year for ContainsYear to detect.
        const string query = "Honda CBR specifications and reviews";

        var result = _sut.GenerateNoResultsResponse(query);

        result.Should().Contain("Include year");
    }

    [Fact]
    public void GenerateNoResultsResponse_QueryWithYear_OmitsYearSuggestion()
    {
        const string query = "Honda CBR1000RR 2023 specifications";

        var result = _sut.GenerateNoResultsResponse(query);

        result.Should().NotContain("Include year");
    }
}
