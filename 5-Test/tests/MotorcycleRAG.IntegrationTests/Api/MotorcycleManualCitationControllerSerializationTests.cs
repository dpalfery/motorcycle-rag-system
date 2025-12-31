using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Domain.DTOs;
using Xunit;
using FluentAssertions;
using MotorcycleRAG.IntegrationTests;

/// <summary>
/// Controller serialization tests for manual PDF citation locators.
/// Tests API endpoint serialization/deserialization with mocked service responses.
/// 
/// NOTE: This is NOT a true integration test. The service layer is mocked.
/// For true component tests exercising real PDF processing → citation mapping flow,
/// see MotorcycleManualCitationComponentTests.cs.
/// 
/// What's tested here:
/// - API endpoint receives and serializes ManualPdfCitationLocator correctly
/// - Response structure matches expected format
/// 
/// What's NOT tested here:
/// - Real PDF processing logic
/// - Real citation mapping from search results
/// - Real locator metadata extraction
/// </summary>
public class MotorcycleManualCitationControllerSerializationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MotorcycleManualCitationControllerSerializationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> CreateFactoryWithMockedService()
    {
        // Override IMotorcycleRAGService with a mocked implementation for serialization testing
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove existing registration (if any)
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IMotorcycleRAGService));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                // Register mock service with manual citation responses for serialization testing
                var mockService = new Mock<IMotorcycleRAGService>();

                mockService.Setup(s => s.QueryAsync(It.IsAny<MotorcycleQueryRequest>()))
                            .ReturnsAsync((MotorcycleQueryRequest r) => CreateManualCitationResponse(r.Query));

                mockService.Setup(s => s.GetHealthAsync())
                            .ReturnsAsync(new HealthCheckResult { IsHealthy = true, Status = "OK" });

                services.AddSingleton(mockService.Object);
            });
        });
    }

    [Fact]
    public async Task QueryEndpoint_SerializesManualCitationWithLocatorMetadata_Correctly()
    {
        // Arrange
        var factory = CreateFactoryWithMockedService();
        var client = factory.CreateClientWithRoles("User");
        var request = new MotorcycleQueryRequest
        {
            Query = "How do I change the oil on a Honda CBR1000RR?"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/motorcycles/query", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>(GetJsonOptions());
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Sources);

        // Verify at least one source has a manual citation with locator metadata
        var manualCitations = body.Sources
            .Where(s => s.Source.Citation != null)
            .Select(s => s.Source.Citation!)
            .Where(c => c.SourceType == CitationSourceType.ManualPdf)
            .ToList();

        manualCitations.Should().NotBeEmpty("because at least one ManualPdf citation is expected in response");

        // Verify locator metadata is present and serializes correctly
        foreach (var citation in manualCitations)
        {
            Assert.NotNull(citation.Locator);
            var locator = citation.Locator switch
            {
                ManualPdfCitationLocator typed => typed,
                JsonElement json => JsonSerializer.Deserialize<ManualPdfCitationLocator>(json.GetRawText(), GetJsonOptions())!,
                _ => throw new InvalidOperationException($"Unexpected locator type: {citation.Locator.GetType().FullName}")
            };

            // Assert required locator fields are populated
            locator.DocumentId.Should().NotBeNullOrWhiteSpace("because DocumentId should be populated for manual citations");
            locator.Title.Should().NotBeNullOrWhiteSpace("because Title should be populated for manual citations");
            
            // Assert at least PageNumber OR PageRange is present
            (locator.PageNumber > 0 || !string.IsNullOrWhiteSpace(locator.PageRange))
                .Should().BeTrue("because at least PageNumber or PageRange should be populated");

            // Assert section information is available (best-effort)
            // Either PrimarySection or legacy Section should be present
            (!string.IsNullOrWhiteSpace(locator.PrimarySection) || !string.IsNullOrWhiteSpace(locator.Section))
                .Should().BeTrue("because Section information (PrimarySection or Section) should be available");
        }
    }

    [Fact]
    public async Task QueryEndpoint_SerializesMultipleManualCitationsWithDifferentLocators_Correctly()
    {
        // Arrange
        var factory = CreateFactoryWithMockedService();
        var client = factory.CreateClientWithRoles("User");
        var request = new MotorcycleQueryRequest
        {
            Query = "What are the brake maintenance procedures for Ducati Panigale V4?"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/motorcycles/query", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>(GetJsonOptions());
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Sources);

        // Verify multiple manual citations can be present with different locators
        var manualCitations = body.Sources
            .Where(s => s.Source.Citation != null && s.Source.Citation.SourceType == CitationSourceType.ManualPdf)
            .Select(s =>
            {
                var locatorObj = s.Source.Citation!.Locator!;
                return locatorObj switch
                {
                    ManualPdfCitationLocator typed => typed,
                    JsonElement json => JsonSerializer.Deserialize<ManualPdfCitationLocator>(json.GetRawText(), GetJsonOptions())!,
                    _ => throw new InvalidOperationException($"Unexpected locator type: {locatorObj.GetType().FullName}")
                };
            })
            .ToList();

        manualCitations.Count.Should().BeGreaterThanOrEqualTo(2, "because at least 2 manual citations are expected for comprehensive query");

        // Verify each locator has distinct page information
        var pageNumbers = manualCitations
            .Where(l => l.PageNumber > 0)
            .Select(l => l.PageNumber)
            .Distinct()
            .ToList();

        pageNumbers.Count.Should().BeGreaterThanOrEqualTo(2, "because citations from different pages are expected");
    }

    [Fact]
    public async Task QueryEndpoint_SerializesSectionHierarchyInLocator_Correctly()
    {
        // Arrange
        var factory = CreateFactoryWithMockedService();
        var client = factory.CreateClientWithRoles("User");
        var request = new MotorcycleQueryRequest
        {
            Query = "What are the suspension adjustment procedures?"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/motorcycles/query", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>(GetJsonOptions());
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Sources);

        // Verify section hierarchy is included in locator metadata
        var manualCitation = body.Sources
            .FirstOrDefault(s => s.Source.Citation != null && s.Source.Citation.SourceType == CitationSourceType.ManualPdf);

        Assert.NotNull(manualCitation);
        Assert.NotNull(manualCitation!.Source.Citation!.Locator);
        
        var locatorObj = manualCitation.Source.Citation.Locator!;
        var locator = locatorObj switch
        {
            ManualPdfCitationLocator typed => typed,
            JsonElement json => JsonSerializer.Deserialize<ManualPdfCitationLocator>(json.GetRawText(), GetJsonOptions())!,
            _ => throw new InvalidOperationException($"Unexpected locator type: {locatorObj.GetType().FullName}")
        };

        // Verify section-level information is present
        locator.SectionLevel.Should().NotBeNull();
        locator.SectionLevel.Should().BeInRange(1, 3,
            "because SectionLevel should be between 1 (Chapter) and 3 (Subsection)");

        // Verify section headings array is populated
        locator.SectionHeadings.Should().NotBeNull();
        locator.SectionHeadings.Length.Should().BeGreaterThan(0,
            "because SectionHeadings array should contain at least one entry");

        // Verify PrimarySection matches the first section heading
        locator.PrimarySection.Should().Be(locator.SectionHeadings[0],
            "because PrimarySection should match the first entry in SectionHeadings");
    }

    [Fact]
    public async Task QueryEndpoint_SerializesPageRangeInLocator_Correctly()
    {
        // Arrange
        var factory = CreateFactoryWithMockedService();
        var client = factory.CreateClientWithRoles("User");
        var request = new MotorcycleQueryRequest
        {
            Query = "Show me the complete engine disassembly procedure"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/motorcycles/query", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>(GetJsonOptions());
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Sources);

        // Find a citation with page range (multi-page content)
        var multiPageCitation = body.Sources
            .Where(s => s.Source.Citation != null && s.Source.Citation.SourceType == CitationSourceType.ManualPdf)
            .Select(s =>
            {
                var locatorObj = s.Source.Citation!.Locator!;
                return locatorObj switch
                {
                    ManualPdfCitationLocator typed => typed,
                    JsonElement json => JsonSerializer.Deserialize<ManualPdfCitationLocator>(json.GetRawText(), GetJsonOptions())!,
                    _ => throw new InvalidOperationException($"Unexpected locator type: {locatorObj.GetType().FullName}")
                };
            })
            .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.PageRange));

        multiPageCitation.Should().NotBeNull("because at least one citation with PageRange is expected for multi-page content");
        
        // Verify page range format
        multiPageCitation!.PageRange.Should().MatchRegex(@"^\d+(-\d+)?$",
            "because PageRange should be in format 'N' or 'N-M'");
    }

    [Fact]
    public async Task QueryEndpoint_SerializesMixedManualAndDatasetCitations_Correctly()
    {
        // Arrange
        var factory = CreateFactoryWithMockedService();
        var client = factory.CreateClientWithRoles("User");
        var request = new MotorcycleQueryRequest
        {
            Query = "What are the specifications and maintenance procedures for Kawasaki Ninja ZX-10R?"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/motorcycles/query", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>(GetJsonOptions());
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Sources);

        // Verify both ManualPdf and Dataset citations are present
        var hasManualCitation = body.Sources
            .Any(s => s.Source.Citation != null && s.Source.Citation.SourceType == CitationSourceType.ManualPdf);
        
        var hasDatasetCitation = body.Sources
            .Any(s => s.Source.Citation != null && s.Source.Citation.SourceType == CitationSourceType.Dataset);

        hasManualCitation.Should().BeTrue("because ManualPdf citation is expected for maintenance procedures");
        hasDatasetCitation.Should().BeTrue("because Dataset citation is expected for specifications");

        // Verify manual citation has ManualPdfCitationLocator
        var manualCitation = body.Sources
            .First(s => s.Source.Citation != null && s.Source.Citation.SourceType == CitationSourceType.ManualPdf);

        manualCitation.Source.Citation!.Locator.Should().NotBeNull();

        // Verify dataset citation has DatasetCitationLocator
        var datasetCitation = body.Sources
            .First(s => s.Source.Citation != null && s.Source.Citation.SourceType == CitationSourceType.Dataset);

        var datasetLocatorObj = datasetCitation.Source.Citation!.Locator!;
        var datasetLocator = datasetLocatorObj switch
        {
            DatasetCitationLocator typed => typed,
            JsonElement json => JsonSerializer.Deserialize<DatasetCitationLocator>(json.GetRawText(), GetJsonOptions())!,
            _ => throw new InvalidOperationException($"Unexpected locator type: {datasetLocatorObj.GetType().FullName}")
        };
        datasetLocator.Should().NotBeNull();
    }

    /// <summary>
    /// Creates a mock response with manual citation locators for serialization testing.
    /// This is NOT testing real PDF processing or citation mapping logic.
    /// </summary>
    private static JsonSerializerOptions GetJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static MotorcycleQueryResponse CreateManualCitationResponse(string query)
    {
        return new MotorcycleQueryResponse
        {
            QueryId = Guid.NewGuid().ToString("N"),
            Response = $"Based on the maintenance manual, here's the procedure for: {query}",
            GeneratedAt = DateTime.UtcNow,
            Sources = new[]
            {
                new SearchResult
                {
                    Id = "doc-001",
                    Content = "Step 1: Remove the drain plug and allow oil to drain completely. Step 2: Replace the oil filter with a new OEM filter.",
                    RelevanceScore = 0.95f,
                    Source = new SearchSource
                    {
                        AgentType = SearchAgentType.PDFSearch,
                        SourceName = "Honda CBR1000RR Service Manual 2024",
                        SourceUrl = "https://manuals.honda.com/cbr1000rr-2024",
                        DocumentId = "manual-honda-cbr1000rr-2024",
                        LastUpdated = DateTime.UtcNow,
                        Citation = new Citation
                        {
                            SourceType = CitationSourceType.ManualPdf,
                            SourceName = "Honda CBR1000RR Service Manual 2024",
                            SourceUrl = "https://manuals.honda.com/cbr1000rr-2024",
                            PageNumber = 45,
                            Section = "Engine Maintenance",
                            ConfidenceScore = 0.95f,
                            Verified = true,
                            VerificationMethod = "Cross-referenced with manufacturer documentation",
                            VerifiedAt = DateTime.UtcNow,
                            Locator = new ManualPdfCitationLocator
                            {
                                DocumentId = "manual-honda-cbr1000rr-2024",
                                Title = "Honda CBR1000RR Service Manual 2024",
                                PageNumber = 45,
                                PageRange = "45-47",
                                PrimarySection = "Engine Maintenance",
                                SectionLevel = 2,
                                SectionHeadings = new[] { "Engine Maintenance", "Oil Change Procedure" },
                                TableCaption = null,
                                ChunkIndex = 1,
                                Section = "Engine Maintenance",
                                FigureReference = "Fig 3.2",
                                Version = "2024.1",
                                PublicationDate = new DateTime(2024, 1, 1),
                                SourceUrl = "https://manuals.honda.com/cbr1000rr-2024"
                            }
                        }
                    }
                },
                new SearchResult
                {
                    Id = "doc-002",
                    Content = "Torque specifications: Oil drain plug: 30 Nm, Oil filter: 20 Nm",
                    RelevanceScore = 0.88f,
                    Source = new SearchSource
                    {
                        AgentType = SearchAgentType.PDFSearch,
                        SourceName = "Honda CBR1000RR Service Manual 2024",
                        SourceUrl = "https://manuals.honda.com/cbr1000rr-2024",
                        DocumentId = "manual-honda-cbr1000rr-2024",
                        LastUpdated = DateTime.UtcNow,
                        Citation = new Citation
                        {
                            SourceType = CitationSourceType.ManualPdf,
                            SourceName = "Honda CBR1000RR Service Manual 2024",
                            SourceUrl = "https://manuals.honda.com/cbr1000rr-2024",
                            PageNumber = 46,
                            Section = "Torque Specifications",
                            ConfidenceScore = 0.88f,
                            Verified = true,
                            VerificationMethod = "Cross-referenced with manufacturer documentation",
                            VerifiedAt = DateTime.UtcNow,
                            Locator = new ManualPdfCitationLocator
                            {
                                DocumentId = "manual-honda-cbr1000rr-2024",
                                Title = "Honda CBR1000RR Service Manual 2024",
                                PageNumber = 46,
                                PageRange = "46",
                                PrimarySection = "Torque Specifications",
                                SectionLevel = 3,
                                SectionHeadings = new[] { "Engine Maintenance", "Torque Specifications" },
                                ChunkIndex = 2,
                                Section = "Torque Specifications",
                                Version = "2024.1",
                                PublicationDate = new DateTime(2024, 1, 1),
                                SourceUrl = "https://manuals.honda.com/cbr1000rr-2024"
                            }
                        }
                    }
                },
                new SearchResult
                {
                    Id = "spec-001",
                    Content = "Engine Type: 999cc liquid-cooled inline four-cylinder",
                    RelevanceScore = 0.85f,
                    Source = new SearchSource
                    {
                        AgentType = SearchAgentType.VectorSearch,
                        SourceName = "Motorcycle Specifications Dataset",
                        SourceUrl = "https://specs.motorcycle-rag.com",
                        DocumentId = "dataset-honda-cbr1000rr",
                        LastUpdated = DateTime.UtcNow,
                        Citation = new Citation
                        {
                            SourceType = CitationSourceType.Dataset,
                            SourceName = "Motorcycle Specifications Dataset",
                            SourceUrl = "https://specs.motorcycle-rag.com",
                            Section = "Engine Specifications",
                            ConfidenceScore = 0.85f,
                            Verified = true,
                            VerificationMethod = "Dataset validation",
                            VerifiedAt = DateTime.UtcNow,
                            Locator = new DatasetCitationLocator
                            {
                                DatasetName = "Motorcycle Specifications Dataset",
                                Version = "2024.12",
                                RecordId = "honda-cbr1000rr-2024",
                                FieldName = "EngineType",
                                DataSourceUrl = "https://specs.motorcycle-rag.com",
                                RetrievalTimestamp = DateTime.UtcNow
                            }
                        }
                    }
                }
            },
            Metrics = new QueryMetrics
            {
                TotalDuration = TimeSpan.FromMilliseconds(250),
                ProcessingTimeMs = 200,
                ResultsFound = 3,
                CacheHit = false
            }
        };
    }
}
