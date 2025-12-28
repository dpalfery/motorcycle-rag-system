using MotorcycleRAG.Contracts.Models;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.UnitTests.Services;

public class ModelValidationServiceTests
{
    private readonly ModelValidationService _validationService;

    public ModelValidationServiceTests()
    {
        _validationService = new ModelValidationService();
    }

    #region Generic Model Validation Tests

    [Fact]
    public void ValidateModel_WithNullModel_ShouldReturnInvalid()
    {
        // Act
        var result = _validationService.ValidateModel<MotorcycleSpecification>(null!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Model cannot be null");
    }

    [Fact]
    public void ValidateModel_WithValidModel_ShouldReturnValid()
    {
        // Arrange
        var specification = CreateValidMotorcycleSpecification();

        // Act
        var result = _validationService.ValidateModel(specification);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region MotorcycleSpecification Validation Tests

    [Fact]
    public void ValidateMotorcycleSpecification_WithValidSpecification_ShouldReturnValid()
    {
        // Arrange
        var specification = CreateValidMotorcycleSpecification();

        // Act
        var result = _validationService.ValidateMotorcycleSpecification(specification);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateMotorcycleSpecification_WithMissingRequiredFields_ShouldReturnInvalid()
    {
        // Arrange
        var specification = new MotorcycleSpecification
        {
            // Missing required fields: Id, Make, Model
            Year = 2023
        };

        // Act
        var result = _validationService.ValidateMotorcycleSpecification(specification);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(0);
        result.Errors.Should().Contain(e => e.Contains("Id"));
        result.Errors.Should().Contain(e => e.Contains("Make"));
        result.Errors.Should().Contain(e => e.Contains("Model"));
    }

    [Fact]
    public void ValidateMotorcycleSpecification_WithInvalidYear_ShouldReturnInvalid()
    {
        // Arrange
        var specification = CreateValidMotorcycleSpecification();
        specification.Year = 1800; // Invalid year

        // Act
        var result = _validationService.ValidateMotorcycleSpecification(specification);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Year"));
    }

    [Fact]
    public void ValidateMotorcycleSpecification_WithInconsistentEngineSpecs_ShouldReturnInvalid()
    {
        // Arrange
        var specification = CreateValidMotorcycleSpecification();
        specification.Engine = new EngineSpecification
        {
            Type = "V-Twin",
            DisplacementCC = 1000,
            Horsepower = 10, // Too low for 1000cc
            Torque = 80,
            FuelSystem = "Fuel Injection",
            Cylinders = 2
        };

        // Act
        var result = _validationService.ValidateMotorcycleSpecification(specification);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("horsepower seems inconsistent"));
    }

    [Fact]
    public void ValidateMotorcycleSpecification_WithFuturePriceDate_ShouldReturnInvalid()
    {
        // Arrange
        var specification = CreateValidMotorcycleSpecification();
        specification.Pricing = new PricingInformation
        {
            MSRP = 15000,
            Currency = "USD",
            PriceDate = DateTime.UtcNow.AddDays(30), // Future date
            Market = "US"
        };

        // Act
        var result = _validationService.ValidateMotorcycleSpecification(specification);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Price date cannot be in the future"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateMotorcycleSpecification_WithInvalidMake_ShouldReturnInvalid(string? make)
    {
        // Arrange
        var specification = CreateValidMotorcycleSpecification();
        specification.Make = make!;

        // Act
        var result = _validationService.ValidateMotorcycleSpecification(specification);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Make"));
    }

    [Fact]
    public void ValidateMotorcycleSpecification_WithTooLongMake_ShouldReturnInvalid()
    {
        // Arrange
        var specification = CreateValidMotorcycleSpecification();
        specification.Make = new string('A', 101); // Exceeds 100 character limit

        // Act
        var result = _validationService.ValidateMotorcycleSpecification(specification);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Make"));
    }

    #endregion

    #region MotorcycleDocument Validation Tests

    [Fact]
    public void ValidateMotorcycleDocument_WithValidDocument_ShouldReturnValid()
    {
        // Arrange
        var document = CreateValidMotorcycleDocument();

        // Act
        var result = _validationService.ValidateMotorcycleDocument(document);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateMotorcycleDocument_WithEmptyContent_ShouldReturnInvalid(string? content)
    {
        // Arrange
        var document = CreateValidMotorcycleDocument();
        document.Content = content!;

        // Act
        var result = _validationService.ValidateMotorcycleDocument(document);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Content field is required") || e.Contains("content cannot be empty"));
    }

    [Fact]
    public void ValidateMotorcycleDocument_WithTooShortContent_ShouldReturnInvalid()
    {
        // Arrange
        var document = CreateValidMotorcycleDocument();
        document.Content = "Short"; // Less than 10 characters

        // Act
        var result = _validationService.ValidateMotorcycleDocument(document);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("content is too short"));
    }

    [Fact]
    public void ValidateMotorcycleDocument_WithTooLongContent_ShouldReturnInvalid()
    {
        // Arrange
        var document = CreateValidMotorcycleDocument();
        document.Content = new string('A', 1000001); // Exceeds 1MB limit

        // Act
        var result = _validationService.ValidateMotorcycleDocument(document);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("content is too large"));
    }

    [Fact]
    public void ValidateMotorcycleDocument_WithEmptyVector_ShouldReturnInvalid()
    {
        // Arrange
        var document = CreateValidMotorcycleDocument();
        document.ContentVector = new float[0]; // Empty vector

        // Act
        var result = _validationService.ValidateMotorcycleDocument(document);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Content vector cannot be empty"));
    }

    [Fact]
    public void ValidateMotorcycleDocument_WithWrongVectorDimensions_ShouldReturnInvalid()
    {
        // Arrange
        var document = CreateValidMotorcycleDocument();
        document.ContentVector = new float[1536]; // Wrong dimensions (should be 3072)

        // Act
        var result = _validationService.ValidateMotorcycleDocument(document);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Content vector must have 3072 dimensions"));
    }

    [Fact]
    public void ValidateMotorcycleDocument_WithCorrectVectorDimensions_ShouldReturnValid()
    {
        // Arrange
        var document = CreateValidMotorcycleDocument();
        document.ContentVector = new float[3072]; // Correct dimensions for text-embedding-3-large

        // Act
        var result = _validationService.ValidateMotorcycleDocument(document);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateMotorcycleDocument_WithInvalidTitle_ShouldReturnInvalid(string? title)
    {
        // Arrange
        var document = CreateValidMotorcycleDocument();
        document.Title = title!;

        // Act
        var result = _validationService.ValidateMotorcycleDocument(document);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Title"));
    }

    [Fact]
    public void ValidateMotorcycleDocument_WithTooLongTitle_ShouldReturnInvalid()
    {
        // Arrange
        var document = CreateValidMotorcycleDocument();
        document.Title = new string('A', 501); // Exceeds 500 character limit

        // Act
        var result = _validationService.ValidateMotorcycleDocument(document);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Title"));
    }

    #endregion

    #region QueryRequest Validation Tests

    [Fact]
    public void ValidateQueryRequest_WithValidRequest_ShouldReturnValid()
    {
        // Arrange
        var request = CreateValidQueryRequest();

        // Act
        var result = _validationService.ValidateQueryRequest(request);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateQueryRequest_WithEmptyQuery_ShouldReturnInvalid(string? query)
    {
        // Arrange
        var request = CreateValidQueryRequest();
        request.Query = query!;

        // Act
        var result = _validationService.ValidateQueryRequest(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Query field is required") || e.Contains("Query cannot be empty"));
    }

    [Fact]
    public void ValidateQueryRequest_WithTooShortQuery_ShouldReturnInvalid()
    {
        // Arrange
        var request = CreateValidQueryRequest();
        request.Query = "Hi"; // Less than 3 characters

        // Act
        var result = _validationService.ValidateQueryRequest(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Query is too short"));
    }

    [Fact]
    public void ValidateQueryRequest_WithTooLongQuery_ShouldReturnInvalid()
    {
        // Arrange
        var request = CreateValidQueryRequest();
        request.Query = new string('A', 1001); // Exceeds 1000 character limit

        // Act
        var result = _validationService.ValidateQueryRequest(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Query"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateQueryRequest_WithInvalidMaxResults_ShouldReturnInvalid(int maxResults)
    {
        // Arrange
        var request = CreateValidQueryRequest();
        request.Preferences.MaxResults = maxResults;

        // Act
        var result = _validationService.ValidateQueryRequest(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MaxResults must be greater than 0"));
    }

    [Fact]
    public void ValidateQueryRequest_WithTooHighMaxResults_ShouldReturnInvalid()
    {
        // Arrange
        var request = CreateValidQueryRequest();
        request.Preferences.MaxResults = 101; // Exceeds limit of 100

        // Act
        var result = _validationService.ValidateQueryRequest(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MaxResults cannot exceed 100"));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void ValidateQueryRequest_WithInvalidMinRelevanceScore_ShouldReturnInvalid(float minScore)
    {
        // Arrange
        var request = CreateValidQueryRequest();
        request.Preferences.MinRelevanceScore = minScore;

        // Act
        var result = _validationService.ValidateQueryRequest(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MinRelevanceScore must be between 0 and 1"));
    }

    [Fact]
    public void ValidateQueryRequest_WithTooLongUserId_ShouldReturnInvalid()
    {
        // Arrange
        var request = CreateValidQueryRequest();
        request.UserId = new string('A', 101); // Exceeds 100 character limit

        // Act
        var result = _validationService.ValidateQueryRequest(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("UserId"));
    }

    #endregion

    #region Verification and Citation Tests

    [Fact]
    public void ValidateQueryResponse_WithCitations_ShouldReturnValid()
    {
        // Arrange
        var response = CreateValidQueryResponseWithCitations();

        // Act
        var result = _validationService.ValidateModel(response);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateQueryResponse_WithMissingCitations_ShouldReturnValid()
    {
        // Arrange
        var response = CreateValidQueryResponseWithCitations();
        // Remove citations from sources - this is allowed since Citation is nullable
        foreach (var source in response.Sources)
        {
            source.Source.Citation = null;
        }

        // Act
        var result = _validationService.ValidateModel(response);

        // Assert - Citation is nullable, so missing citations should still be valid
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateQueryResponse_WithInvalidQueryId_ShouldReturnInvalid()
    {
        // Arrange
        var response = CreateValidQueryResponseWithCitations();
        response.QueryId = ""; // Invalid empty query ID

        // Act
        var result = _validationService.ValidateModel(response);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("QueryId"));
    }

    [Fact]
    public void ValidateQueryResponse_WithMissingMetrics_ShouldReturnValid()
    {
        // Arrange
        var response = CreateValidQueryResponseWithCitations();
        response.Metrics = null!;

        // Act
        var result = _validationService.ValidateModel(response);

        // Assert - Metrics is not marked as [Required], so null is valid
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    #endregion

    #region Helper Methods

    private static MotorcycleSpecification CreateValidMotorcycleSpecification()
    {
        return new MotorcycleSpecification
        {
            Id = "test-id-123",
            Make = "Honda",
            Model = "CBR1000RR",
            Year = 2023,
            Engine = new EngineSpecification
            {
                Type = "Inline-4",
                DisplacementCC = 1000,
                Horsepower = 200,
                Torque = 113,
                FuelSystem = "Fuel Injection",
                Cylinders = 4
            },
            Performance = new PerformanceMetrics
            {
                TopSpeedKmh = 299,
                Acceleration0To100 = 3.1m,
                FuelConsumptionL100km = 6.5m,
                RangeKm = 300
            },
            Safety = new SafetyFeatures
            {
                ABS = true,
                TractionControl = true,
                StabilityControl = true,
                AntiWheelieControl = true,
                AdditionalFeatures = new List<string> { "Cornering ABS", "Wheelie Control" }
            },
            Pricing = new PricingInformation
            {
                MSRP = 17999,
                Currency = "USD",
                PriceDate = DateTime.UtcNow.AddDays(-1),
                Market = "US"
            },
            AdditionalSpecs = new Dictionary<string, object>
            {
                { "Color", "Racing Red" },
                { "Weight", 201 }
            }
        };
    }

    private static MotorcycleDocument CreateValidMotorcycleDocument()
    {
        return new MotorcycleDocument
        {
            Id = "doc-123",
            Title = "Honda CBR1000RR Specifications",
            Content = "This is a detailed specification document for the Honda CBR1000RR motorcycle.",
            Type = DocumentType.Specification,
            Metadata = new DocumentMetadata
            {
                SourceFile = "honda-cbr1000rr.pdf",
                SourceUrl = "https://example.com/honda-cbr1000rr.pdf",
                PageNumber = 1,
                Section = "Specifications",
                Author = "Honda Motor Co.",
                PublishedDate = DateTime.UtcNow.AddDays(-30),
                Tags = new List<string> { "Honda", "CBR1000RR", "Specifications" },
                AdditionalProperties = new Dictionary<string, object>
                {
                    { "Language", "English" },
                    { "Version", "2023.1" }
                }
            },
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static MotorcycleQueryRequest CreateValidQueryRequest()
    {
        return new MotorcycleQueryRequest
        {
            Query = "What are the specifications of Honda CBR1000RR?",
            UserId = "user-123",
            Preferences = new SearchPreferences
            {
                IncludeWebSources = true,
                IncludePDFSources = true,
                MaxResults = 10,
                MinRelevanceScore = 0.5f,
                PreferredSources = new List<string> { "Honda", "Official" }
            },
            Context = new QueryContext
            {
                SessionId = "session-123",
                PreviousQueries = new List<string> { "Honda motorcycles" },
                UserPreferences = new Dictionary<string, object>
                {
                    { "PreferredBrand", "Honda" }
                },
                Language = "en"
            }
        };
    }

    private static MotorcycleQueryResponse CreateValidQueryResponseWithCitations()
    {
        return new MotorcycleQueryResponse
        {
            QueryId = Guid.NewGuid().ToString("N"),
            Response = "The Honda CBR1000RR has a 1000cc inline-4 engine producing 200 horsepower.",
            GeneratedAt = DateTime.UtcNow,
            Sources = new[]
            {
                new SearchResult
                {
                    Id = "1",
                    Content = "Honda CBR1000RR specifications: 1000cc inline-4 engine, 200 horsepower",
                    RelevanceScore = 0.95f,
                    Source = new SearchSource
                    {
                        AgentType = SearchAgentType.VectorSearch,
                        SourceName = "Honda Official Specifications",
                        SourceUrl = "https://www.honda.com/cbr1000rr/specs",
                        DocumentId = "honda-cbr1000rr-2023",
                        LastUpdated = DateTime.UtcNow.AddDays(-30),
                        Citation = new Citation
                        {
                            SourceType = CitationSourceType.Dataset,
                            SourceName = "Honda Official Specifications",
                            SourceUrl = "https://www.honda.com/cbr1000rr/specs",
                            PageNumber = 1,
                            Section = "Engine Specifications",
                            ConfidenceScore = 0.95f,
                            Verified = true,
                            VerificationMethod = "Cross-referenced with manufacturer data",
                            Locator = new DatasetCitationLocator
                            {
                                DatasetName = "Honda Motorcycle Specifications 2023",
                                Version = "2023.1",
                                RecordId = "CBR1000RR-2023-001",
                                FieldName = "Engine.Horsepower",
                                DataSourceUrl = "https://www.honda.com/api/specs/v1/motorcycles",
                                RetrievalTimestamp = DateTime.UtcNow.AddDays(-1)
                            }
                        }
                    },
                    Metadata = new Dictionary<string, object>
                    {
                        { "Brand", "Honda" },
                        { "Model", "CBR1000RR" },
                        { "Year", 2023 }
                    }
                }
            },
            Metrics = new QueryMetrics
            {
                TotalDuration = TimeSpan.FromSeconds(1.5),
                VectorSearchDuration = TimeSpan.FromMilliseconds(800),
                WebSearchDuration = TimeSpan.FromMilliseconds(200),
                PDFSearchDuration = TimeSpan.FromMilliseconds(0),
                TokensUsed = 150,
                EstimatedCost = 0.0025m,
                ResultsFound = 1,
                ProcessingTimeMs = 1500,
                CacheHit = false,
                MultiModalProcessed = false,
                SourcesSearched = 2,
                SearchPattern = new SearchPatternMetrics
                {
                    VectorSearchExecuted = true,
                    WebSearchExecuted = true,
                    PDFSearchExecuted = false,
                    VectorSearchTime = TimeSpan.FromMilliseconds(800),
                    WebSearchTime = TimeSpan.FromMilliseconds(200),
                    PDFSearchTime = TimeSpan.FromMilliseconds(0),
                    VectorResultsFound = 1,
                    WebResultsFound = 0,
                    PDFResultsFound = 0
                }
            }
        };
    }

    #endregion
}
