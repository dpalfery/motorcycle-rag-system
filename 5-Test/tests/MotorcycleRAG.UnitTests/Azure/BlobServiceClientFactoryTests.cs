using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Azure;

namespace MotorcycleRAG.UnitTests.Azure;

public sealed class BlobServiceClientFactoryTests
{
    [Fact]
    public void Create_WithDevelopmentConnectionString_UsesAzuriteConnectionString()
    {
        var client = BlobServiceClientFactory.Create(
            new BlobStorageOptions { ConnectionString = "UseDevelopmentStorage=true" },
            new TestHostEnvironment(Environments.Development));

        client.Uri.Host.Should().Be("127.0.0.1");
        client.Uri.AbsolutePath.Should().Contain("devstoreaccount1");
    }

    [Fact]
    public void Create_WithProductionConnectionString_Throws()
    {
        var act = () => BlobServiceClientFactory.Create(
            new BlobStorageOptions { ConnectionString = "UseDevelopmentStorage=true" },
            new TestHostEnvironment(Environments.Production));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*allowed only in Development*");
    }

    [Fact]
    public void Create_WithoutConnectionString_UsesAccountEndpoint()
    {
        var client = BlobServiceClientFactory.Create(
            new BlobStorageOptions { AccountEndpoint = "https://storage.example.blob.core.windows.net" },
            new TestHostEnvironment(Environments.Production));

        client.Uri.Should().Be(new Uri("https://storage.example.blob.core.windows.net/"));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "MotorcycleRAG.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
