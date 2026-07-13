using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Persistence.Azure;
using MotorcycleRAG.Persistence.Local;

namespace MotorcycleRAG.Persistence.Tests.Local;

public sealed class LocalFileAdaptersTests
{
    [Fact]
    public async Task LocalFileStore_WhenWritingReadingAndDeletingFile_PersistsExpectedContent()
    {
        var rootDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalFileStore();
            var uploadDirectory = Path.Combine(rootDirectory, "uploads");
            var filePath = Path.Combine(uploadDirectory, "manual.pdf");
            var expectedContent = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4\ncontent");

            await store.EnsureDirectoryExistsAsync(uploadDirectory);
            await using var source = new MemoryStream(expectedContent);
            await store.WriteAsync(filePath, source);

            var actualContent = await store.ReadAllBytesIfExistsAsync(filePath);
            var wasDeleted = await store.DeleteIfExistsAsync(filePath);
            var wasDeletedAgain = await store.DeleteIfExistsAsync(filePath);

            actualContent.Should().Equal(expectedContent);
            wasDeleted.Should().BeTrue();
            wasDeletedAgain.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task LocalFileDiscovery_WhenDirectoryContainsNestedFiles_ReturnsOnlyTopLevelPaths()
    {
        var rootDirectory = CreateTemporaryDirectory();
        try
        {
            var topLevelFile = Path.Combine(rootDirectory, "manual.pdf");
            var nestedDirectory = Path.Combine(rootDirectory, "nested");
            Directory.CreateDirectory(nestedDirectory);
            await File.WriteAllTextAsync(topLevelFile, "%PDF-1.4");
            await File.WriteAllTextAsync(Path.Combine(nestedDirectory, "ignored.csv"), "Make,Model");

            var paths = await new LocalFileDiscovery().GetTopLevelFilePathsAsync(rootDirectory);

            paths.Should().ContainSingle().Which.Should().Be(topLevelFile);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void AddAzureServices_RegistersLocalFileContractsAsSingletons()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sql:ConnectionString"] = "Server=.;Database=test"
            })
            .Build();

        services.AddAzureServices(configuration);

        services.Should().Contain(service =>
            service.ServiceType == typeof(ILocalFileStore)
            && service.ImplementationType == typeof(LocalFileStore)
            && service.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(service =>
            service.ServiceType == typeof(ILocalFileDiscovery)
            && service.ImplementationType == typeof(LocalFileDiscovery)
            && service.Lifetime == ServiceLifetime.Singleton);
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"LocalFileAdaptersTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
