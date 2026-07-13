using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Azure;

namespace MotorcycleRAG.UnitTests.Azure;

public sealed class BlobServiceClientFactoryTests
{
    private readonly BlobServiceClientFactory _factory = new();

    [Fact]
    public void Create_WithConnectionString_ReturnsClientBoundToConnectionString()
    {
        var client = _factory.Create("UseDevelopmentStorage=true");

        client.Uri.Host.Should().Be("127.0.0.1");
        client.Uri.AbsolutePath.Should().Contain("devstoreaccount1");
    }

    [Fact]
    public void Create_WithNullConnectionString_Throws()
    {
        var act = () => _factory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_WithCredentialAndEndpoint_ReturnsClientBoundToEndpoint()
    {
        var client = _factory.Create(
            new DefaultAzureCredential(),
            new Uri("https://storage.example.blob.core.windows.net"));

        client.Uri.Should().Be(new Uri("https://storage.example.blob.core.windows.net/"));
    }

    [Fact]
    public void Create_WithNullCredential_Throws()
    {
        var act = () => _factory.Create(null!, new Uri("https://storage.example.blob.core.windows.net"));
        act.Should().Throw<ArgumentNullException>().WithParameterName("credential");
    }

    [Fact]
    public void Create_WithNullEndpoint_Throws()
    {
        var act = () => _factory.Create(new DefaultAzureCredential(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("endpoint");
    }

    [Fact]
    public void CreateFromOptions_DelegatesToFactory_ForConnectionString()
    {
        var mockFactory = new Mock<IBlobServiceClientFactory>();
        var expectedClient = new BlobServiceClient("UseDevelopmentStorage=true");
        mockFactory.Setup(f => f.Create("UseDevelopmentStorage=true")).Returns(expectedClient);

        var client = BlobServiceClientFactory.CreateFromOptions(
            new BlobStorageOptions { ConnectionString = "UseDevelopmentStorage=true" },
            new TestHostEnvironment(Environments.Development),
            mockFactory.Object);

        client.Should().BeSameAs(expectedClient);
        mockFactory.Verify(f => f.Create("UseDevelopmentStorage=true"), Times.Once);
    }

    [Fact]
    public void CreateFromOptions_DelegatesToFactory_ForEndpoint()
    {
        var mockFactory = new Mock<IBlobServiceClientFactory>();
        var expectedClient = new BlobServiceClient("UseDevelopmentStorage=true");
        mockFactory.Setup(f => f.Create(It.IsAny<TokenCredential>(), It.IsAny<Uri>()))
            .Returns(expectedClient);

        var client = BlobServiceClientFactory.CreateFromOptions(
            new BlobStorageOptions { AccountEndpoint = "https://storage.example.blob.core.windows.net" },
            new TestHostEnvironment(Environments.Production),
            mockFactory.Object);

        client.Should().BeSameAs(expectedClient);
        mockFactory.Verify(
            f => f.Create(It.IsAny<TokenCredential>(), It.Is<Uri>(u => u == new Uri("https://storage.example.blob.core.windows.net"))),
            Times.Once);
    }

    [Fact]
    public void CreateFromOptions_ThrowsWhenConnectionStringUsedOutsideDevelopment()
    {
        var mockFactory = new Mock<IBlobServiceClientFactory>();
        var act = () => BlobServiceClientFactory.CreateFromOptions(
            new BlobStorageOptions { ConnectionString = "UseDevelopmentStorage=true" },
            new TestHostEnvironment(Environments.Production),
            mockFactory.Object);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*allowed only in Development*");
        mockFactory.Verify(f => f.Create(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CreateFromOptions_ThrowsWhenNoConnectionConfigured()
    {
        var mockFactory = new Mock<IBlobServiceClientFactory>();
        var act = () => BlobServiceClientFactory.CreateFromOptions(
            new BlobStorageOptions(),
            new TestHostEnvironment(Environments.Production),
            mockFactory.Object);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AccountEndpoint*required*");
        mockFactory.Verify(
            f => f.Create(It.IsAny<string>()), Times.Never);
        mockFactory.Verify(
            f => f.Create(It.IsAny<TokenCredential>(), It.IsAny<Uri>()), Times.Never);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "MotorcycleRAG.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
