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

    // ─────────────────────────────────────────────────────────────────
    // LocalFileStore — argument validation (EnsureDirectoryExistsAsync)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LocalFileStore_EnsureDirectoryExistsAsync_WhenPathIsNull_ThrowsArgumentException()
    {
        var store = new LocalFileStore();

        var act = () => store.EnsureDirectoryExistsAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("directoryPath");
    }

    [Fact]
    public async Task LocalFileStore_EnsureDirectoryExistsAsync_WhenPathIsEmpty_ThrowsArgumentException()
    {
        var store = new LocalFileStore();

        var act = () => store.EnsureDirectoryExistsAsync("");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("directoryPath");
    }

    [Fact]
    public async Task LocalFileStore_EnsureDirectoryExistsAsync_WhenPathIsWhitespace_ThrowsArgumentException()
    {
        var store = new LocalFileStore();

        var act = () => store.EnsureDirectoryExistsAsync("   ");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("directoryPath");
    }

    [Fact]
    public async Task LocalFileStore_EnsureDirectoryExistsAsync_WhenTokenIsCancelled_ThrowsOperationCanceledException()
    {
        var store = new LocalFileStore();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => store.EnsureDirectoryExistsAsync("/some/path", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─────────────────────────────────────────────────────────────────
    // LocalFileStore — argument validation (WriteAsync)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LocalFileStore_WriteAsync_WhenFilePathIsNull_ThrowsArgumentException()
    {
        var store = new LocalFileStore();

        var act = () => store.WriteAsync(null!, Stream.Null);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("filePath");
    }

    [Fact]
    public async Task LocalFileStore_WriteAsync_WhenFilePathIsEmpty_ThrowsArgumentException()
    {
        var store = new LocalFileStore();

        var act = () => store.WriteAsync("", Stream.Null);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("filePath");
    }

    [Fact]
    public async Task LocalFileStore_WriteAsync_WhenContentIsNull_ThrowsArgumentNullException()
    {
        var rootDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalFileStore();
            var filePath = Path.Combine(rootDirectory, "test.pdf");

            var act = () => store.WriteAsync(filePath, null!);

            await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("content");
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task LocalFileStore_WriteAsync_WhenFilePathHasNoDirectory_ThrowsArgumentException()
    {
        var store = new LocalFileStore();

        // A bare filename with no directory component
        var act = () => store.WriteAsync("barefile.pdf", Stream.Null);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("filePath")
            .Where(ex => ex.Message.Contains("must include a directory"));
    }

    // ─────────────────────────────────────────────────────────────────
    // LocalFileStore — argument validation (ReadAllBytesIfExistsAsync)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LocalFileStore_ReadAllBytesIfExistsAsync_WhenFilePathIsNull_ThrowsArgumentException()
    {
        var store = new LocalFileStore();

        var act = () => store.ReadAllBytesIfExistsAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("filePath");
    }

    [Fact]
    public async Task LocalFileStore_ReadAllBytesIfExistsAsync_WhenFileDoesNotExist_ReturnsNull()
    {
        var rootDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalFileStore();
            var nonExistentPath = Path.Combine(rootDirectory, "does-not-exist.pdf");

            var result = await store.ReadAllBytesIfExistsAsync(nonExistentPath);

            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task LocalFileStore_ReadAllBytesIfExistsAsync_WhenTokenIsCancelled_ThrowsOperationCanceledException()
    {
        var store = new LocalFileStore();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => store.ReadAllBytesIfExistsAsync("/some/path", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─────────────────────────────────────────────────────────────────
    // LocalFileStore — argument validation (DeleteIfExistsAsync)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LocalFileStore_DeleteIfExistsAsync_WhenFilePathIsNull_ThrowsArgumentException()
    {
        var store = new LocalFileStore();

        var act = () => store.DeleteIfExistsAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("filePath");
    }

    [Fact]
    public async Task LocalFileStore_DeleteIfExistsAsync_WhenFileDoesNotExist_ReturnsFalse()
    {
        var rootDirectory = CreateTemporaryDirectory();
        try
        {
            var store = new LocalFileStore();
            var nonExistentPath = Path.Combine(rootDirectory, "does-not-exist.pdf");

            var result = await store.DeleteIfExistsAsync(nonExistentPath);

            result.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task LocalFileStore_DeleteIfExistsAsync_WhenTokenIsCancelled_ThrowsOperationCanceledException()
    {
        var store = new LocalFileStore();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => store.DeleteIfExistsAsync("/some/path", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─────────────────────────────────────────────────────────────────
    // LocalFileStore — interface implementation
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void LocalFileStore_Implements_ILocalFileStore()
    {
        var store = new LocalFileStore();
        store.Should().BeAssignableTo<ILocalFileStore>();
    }

    // ─────────────────────────────────────────────────────────────────
    // LocalFileDiscovery — argument validation
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LocalFileDiscovery_GetTopLevelFilePathsAsync_WhenDirectoryPathIsNull_ThrowsArgumentException()
    {
        var discovery = new LocalFileDiscovery();

        var act = () => discovery.GetTopLevelFilePathsAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("directoryPath");
    }

    [Fact]
    public async Task LocalFileDiscovery_GetTopLevelFilePathsAsync_WhenDirectoryPathIsEmpty_ThrowsArgumentException()
    {
        var discovery = new LocalFileDiscovery();

        var act = () => discovery.GetTopLevelFilePathsAsync("");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("directoryPath");
    }

    [Fact]
    public async Task LocalFileDiscovery_GetTopLevelFilePathsAsync_WhenTokenIsCancelled_ThrowsOperationCanceledException()
    {
        var discovery = new LocalFileDiscovery();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => discovery.GetTopLevelFilePathsAsync("/some/path", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task LocalFileDiscovery_GetTopLevelFilePathsAsync_WhenDirectoryIsEmpty_ReturnsEmptyList()
    {
        var rootDirectory = CreateTemporaryDirectory();
        try
        {
            var discovery = new LocalFileDiscovery();

            var paths = await discovery.GetTopLevelFilePathsAsync(rootDirectory);

            paths.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // LocalFileDiscovery — interface implementation
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void LocalFileDiscovery_Implements_ILocalFileDiscovery()
    {
        var discovery = new LocalFileDiscovery();
        discovery.Should().BeAssignableTo<ILocalFileDiscovery>();
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"LocalFileAdaptersTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
