using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MotorcycleRAG.Application.Pipeline;
using MotorcycleRAG.Contracts.Interfaces;

namespace MotorcycleRAG.IntegrationTests.Api;

/// <summary>
/// TDD-RED integration tests for the GET /api/manuals/{manualId}/pages/{pageNumber} endpoint.
/// Validates authentication, authorization, input validation, and response contract.
/// Tests will fail (RED) until ManualsController and IManualPageQueryService are wired in DI.
/// </summary>
public class ManualPageRetrievalAuthorizationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    /// <summary>Minimal valid 1x1 white PNG (67 bytes).</summary>
    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // 1x1
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, // 8-bit RGB
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, // IDAT chunk
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00, // compressed data
        0x00, 0x00, 0x02, 0x00, 0x01, 0xE2, 0x21, 0xBC, // ...
        0x33, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, // IEND
        0x44, 0xAE, 0x42, 0x60, 0x82                      // IEND CRC
    ];

    private static readonly Guid ValidManualId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public ManualPageRetrievalAuthorizationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Test 1: Unauthenticated request returns 401 Unauthorized.
    /// No X-Test-Auth header is sent.
    /// </summary>
    [Fact]
    public async Task GetPage_Unauthenticated_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/manuals/{ValidManualId}/pages/1");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Test 2: Authenticated request with invalid page number (0) returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task GetPage_InvalidPageNumber_Returns400()
    {
        // Arrange
        var client = CreateManualsViewClient();

        // Act
        var response = await client.GetAsync($"/api/manuals/{ValidManualId}/pages/0");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Test 3: Authenticated request for a non-existent page returns 404 Not Found.
    /// The mock IManualPageAssetStore returns false for PageExistsAsync.
    /// </summary>
    [Fact]
    public async Task GetPage_PageNotFound_Returns404()
    {
        // Arrange
        var client = CreateManualsViewClientWithMockedStore(pageExists: false);

        // Act
        var response = await client.GetAsync($"/api/manuals/{ValidManualId}/pages/999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Test 4: Authenticated request for a valid page returns 200 with correct headers.
    /// </summary>
    [Fact]
    public async Task GetPage_ValidRequest_Returns200WithCorrectHeaders()
    {
        // Arrange
        var client = CreateManualsViewClientWithMockedStore(pageExists: true);

        // Act
        var response = await client.GetAsync($"/api/manuals/{ValidManualId}/pages/1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.True(
            response.Headers.CacheControl is not null,
            "Response must include a Cache-Control header");
    }

    // ---- helpers ----

    /// <summary>
    /// Creates an HttpClient authenticated with the mcr-api-manuals-view policy (User role).
    /// </summary>
    private HttpClient CreateManualsViewClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "User");
        return client;
    }

    /// <summary>
    /// Creates an HttpClient with an overridden IManualPageAssetStore and
    /// IManualPageQueryService wired together in DI.
    /// </summary>
    private HttpClient CreateManualsViewClientWithMockedStore(bool pageExists)
    {
        var assetStoreMock = new Mock<IManualPageAssetStore>();

        assetStoreMock
            .Setup(s => s.PageExistsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pageExists);

        if (pageExists)
        {
            assetStoreMock
                .Setup(s => s.DownloadPageAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(OnePixelPng));

            assetStoreMock
                .Setup(s => s.GetETagAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("\"test-etag\"");
        }

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(assetStoreMock.Object);
                services.AddSingleton<IManualPageQueryService, ManualPageQueryService>();
            });
        }).CreateClient();

        client.DefaultRequestHeaders.Add("X-Test-Auth", "User");
        return client;
    }
}