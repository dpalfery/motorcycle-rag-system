using Xunit;
using Moq;
using FluentAssertions;
using MotorcycleRAG.MobileApp.Services;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using MotorcycleRAG.MobileApp.Configuration;

namespace MotorcycleRAG.MobileApp.Tests.Services
{
    public class AuthenticationServiceTests
    {
        [Fact]
        public void Constructor_WithConfiguredPublicClientAndOptions_Initializes()
        {
            // Arrange
            var client = new Mock<IPublicClientApplication>();
            var options = Options.Create(new AuthenticationOptions
            {
                ClientId = "test-client-id",
                TenantId = "test-tenant-id",
                RedirectUri = "msauth://com.companyname.appname",
                Scopes = ["api://motorcyclerag-api/read"],
            });

            // Act
            var service = new AuthenticationService(client.Object, options);

            // Assert
            service.Should().NotBeNull();
        }
    }
}
