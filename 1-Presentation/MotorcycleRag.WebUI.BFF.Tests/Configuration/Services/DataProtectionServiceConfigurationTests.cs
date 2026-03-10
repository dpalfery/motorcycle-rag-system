using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MotorcycleRag.WebUI.BFF.Configuration.Services;
using Xunit;

namespace MotorcycleRag.WebUI.BFF.Tests.Configuration.Services;

public class DataProtectionServiceConfigurationTests
{
    [Fact]
    public void AddBffDataProtection_WithBlobUri_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:BlobUri"] = "https://test.blob.core.windows.net/keys/keys.xml"
            })
            .Build();
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(m => m.EnvironmentName).Returns("Production");

        // Act
        services.AddBffDataProtection(configuration, env.Object);

        // Assert
        // We can't easily verify the internal PersistKeysToAzureBlobStorage call without more complex mocking,
        // but we verify it doesn't throw.
        services.Should().Contain(s => s.ServiceType == typeof(Microsoft.AspNetCore.DataProtection.IDataProtectionProvider));
    }

    [Fact]
    public void AddBffDataProtection_WithoutBlobUri_InDevelopment_Succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(m => m.EnvironmentName).Returns("Development");

        // Act
        services.AddBffDataProtection(configuration, env.Object);

        // Assert
        services.Should().Contain(s => s.ServiceType == typeof(Microsoft.AspNetCore.DataProtection.IDataProtectionProvider));
    }

    [Fact]
    public void AddBffDataProtection_WithoutBlobUri_InProduction_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(m => m.EnvironmentName).Returns("Production");

        // Act
        var act = () => services.AddBffDataProtection(configuration, env.Object);

        // Assert
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*DataProtection:BlobUri is required in production*");
    }
}
