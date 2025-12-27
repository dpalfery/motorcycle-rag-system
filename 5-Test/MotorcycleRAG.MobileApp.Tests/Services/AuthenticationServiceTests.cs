using Xunit;
using Moq;
using FluentAssertions;
using MotorcycleRAG.MobileApp.Services;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

namespace MotorcycleRAG.MobileApp.Tests.Services
{
    public class AuthenticationServiceTests
    {
        // NOTE: AuthenticationService instantiates IPublicClientApplication internally,
        // making it difficult to unit test without refactoring to inject the client.
        // For now, we are skipping deep unit tests for this service and relying on
        // integration/manual tests for the authentication flow.

        [Fact]
        public void Constructor_ShouldInitialize_WithValidConfiguration()
        {
            // Arrange
            var inMemorySettings = new Dictionary<string, string> {
                {"Authentication:ClientId", "test-client-id"},
                {"Authentication:TenantId", "test-tenant-id"},
                {"Authentication:RedirectUri", "msauth://com.companyname.appname"}
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            // Act
            var service = new AuthenticationService(configuration);

            // Assert
            service.Should().NotBeNull();
        }
    }
}
