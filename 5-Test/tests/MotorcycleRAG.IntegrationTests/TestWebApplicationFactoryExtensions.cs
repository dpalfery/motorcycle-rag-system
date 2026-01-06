using Microsoft.AspNetCore.Mvc.Testing;
using MotorcycleRAG.API;

namespace MotorcycleRAG.IntegrationTests;

/// <summary>
/// Extension methods for TestWebApplicationFactory to support role-based authentication in tests
/// </summary>
internal static class TestWebApplicationFactoryExtensions
{
    /// <summary>
    /// Creates an HTTP client with test authentication for DataAdmin role
    /// </summary>
    public static HttpClient CreateDataAdminClient(this WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "DataAdmin");
        return client;
    }

    /// <summary>
    /// Creates an HTTP client with test authentication for Admin role
    /// </summary>
    public static HttpClient CreateAdminClient(this WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "Admin");
        return client;
    }

    /// <summary>
    /// Creates an HTTP client with test authentication for User role
    /// </summary>
    public static HttpClient CreateUserClient(this WebApplicationFactory<Program> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "User");
        return client;
    }

    /// <summary>
    /// Creates an HTTP client with test authentication for multiple roles
    /// </summary>
    public static HttpClient CreateClientWithRoles(this WebApplicationFactory<Program> factory, params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", string.Join(",", roles));
        return client;
    }
}
