using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.API.Middleware;
using Xunit;

namespace MotorcycleRAG.UnitTests.Middleware;

/// <summary>
/// Unit tests for HostHeaderValidationMiddleware
/// Tests Host header injection attack prevention and RFC 7230 compliance
/// </summary>
public class HostHeaderValidationMiddlewareTests
{
    private TestServer CreateTestServer(string allowedHosts)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["AllowedHosts"] = allowedHosts
            })
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddConfiguration(config);

        var app = builder.Build();

        // Register middleware
        app.UseMiddleware<HostHeaderValidationMiddleware>();
        app.MapGet("/test", () => Results.Ok("Success"));

        return new TestServer(app);
    }

    [Fact]
    public async Task ValidHostHeader_ShouldAllowRequest()
    {
        // Arrange
        var server = CreateTestServer("localhost,api.example.com");
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("http://api.example.com/test");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidHostHeader_ShouldReturnBadRequest()
    {
        // Arrange
        var server = CreateTestServer("localhost,api.example.com");
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("http://malicious.example.com/test");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "";
        Assert.Contains("application/problem+json", contentType);
    }

    [Fact]
    public async Task MissingHostHeader_ShouldReturnBadRequest()
    {
        // Arrange
        var server = CreateTestServer("localhost");
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        request.Headers.Remove("Host");
        var client = server.CreateClient();

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EmptyHostHeader_ShouldReturnBadRequest()
    {
        // Arrange
        var server = CreateTestServer("localhost");
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        request.Headers.Host = "";
        var client = server.CreateClient();

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HostWithPort_ShouldValidateHostname()
    {
        // Arrange
        var server = CreateTestServer("api.example.com");
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("http://api.example.com:443/test");

        // Assert
        // Should validate the hostname portion only, ignoring the port
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task IPv6Address_ShouldBeValidated()
    {
        // Arrange
        var server = CreateTestServer("[::1],[2001:db8::1]");
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        request.Headers.Host = "[::1]";
        var client = server.CreateClient();

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MalformedIPv6Address_ShouldReturnBadRequest()
    {
        // Arrange
        var server = CreateTestServer("[::1]");
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");
        request.Headers.Host = "[::1"; // Missing closing bracket
        var client = server.CreateClient();

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CaseInsensitiveHostComparison_ShouldWork()
    {
        // Arrange
        var server = CreateTestServer("API.EXAMPLE.COM");
        var client = server.CreateClient();

        // Act
        var response = await client.GetAsync("http://api.example.com/test");

        // Assert
        // Should allow because comparison is case-insensitive
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void EmptyAllowedHosts_ShouldThrowException()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["AllowedHosts"] = "" // Empty configuration
            })
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddConfiguration(config);
        var app = builder.Build();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            app.UseMiddleware<HostHeaderValidationMiddleware>();
        });
    }

    [Fact]
    public async Task MultipleAllowedHosts_AllShouldBeValidated()
    {
        // Arrange
        var server = CreateTestServer("api.example.com,api-staging.example.com,localhost");
        var client = server.CreateClient();

        // Act - Test first allowed host
        var response1 = await client.GetAsync("http://api.example.com/test");

        // Act - Test second allowed host
        var response2 = await client.GetAsync("http://api-staging.example.com/test");

        // Act - Test third allowed host
        var response3 = await client.GetAsync("http://localhost/test");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, response3.StatusCode);
    }
}
