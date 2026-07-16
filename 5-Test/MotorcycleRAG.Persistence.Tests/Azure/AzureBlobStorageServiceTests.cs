using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Azure;

namespace MotorcycleRAG.Persistence.Tests.Azure;
public class AzureBlobStorageServiceTests
{
    private static IHostEnvironment CreateDevelopmentEnvironment()
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Development");
        return env.Object;
    }

    private static IHostEnvironment CreateProductionEnvironment()
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Production");
        return env.Object;
    }

    private static IOptions<BlobStorageOptions> CreateValidDevelopmentOptions()
        => Options.Create(new BlobStorageOptions { ConnectionString = "UseDevelopmentStorage=true" });

    private static IOptions<BlobStorageOptions> CreateValidProductionOptions()
        => Options.Create(new BlobStorageOptions { AccountEndpoint = "https://storage.example.blob.core.windows.net" });

    private static Mock<IBlobServiceClientFactory> CreateFactoryReturningValidClient()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var client = new BlobServiceClient("UseDevelopmentStorage=true");
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(client);
        factory.Setup(f => f.Create(It.IsAny<TokenCredential>(), It.IsAny<Uri>())).Returns(client);
        return factory;
    }

    private static IAzureCredentialProvider CreateCredentialProvider()
    {
        var provider = new Mock<IAzureCredentialProvider>();
        provider.Setup(x => x.GetDefaultCredential()).Returns(new global::Azure.Identity.DefaultAzureCredential());
        return provider.Object;
    }

    private static AzureBlobStorageService CreateService(
        IOptions<BlobStorageOptions>? options = null,
        IHostEnvironment? environment = null,
        IBlobServiceClientFactory? factory = null)
    {
        return new AzureBlobStorageService(
            options ?? CreateValidDevelopmentOptions(),
            environment ?? CreateDevelopmentEnvironment(),
            factory ?? CreateFactoryReturningValidClient().Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsIsNull()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var act = () => new AzureBlobStorageService(
            null!, CreateDevelopmentEnvironment(), factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenEnvironmentIsNull()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var opts = Options.Create(new BlobStorageOptions { AccountEndpoint = "https://mystorage.blob.core.windows.net" });
        var act = () => new AzureBlobStorageService(
            opts, null!, factory.Object, CreateCredentialProvider(), TestHelpers.CreateNullLogger<AzureBlobStorageService>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("environment");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenFactoryIsNull()
    {
        var opts = CreateValidDevelopmentOptions();
        var act = () => new AzureBlobStorageService(
            opts, CreateDevelopmentEnvironment(), null!,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("blobServiceClientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenCredentialProviderIsNull()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var opts = CreateValidDevelopmentOptions();
        var act = () => new AzureBlobStorageService(
            opts, CreateDevelopmentEnvironment(), factory.Object, null!,
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("credentialProvider");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var opts = CreateValidDevelopmentOptions();
        var act = () => new AzureBlobStorageService(
            opts, CreateDevelopmentEnvironment(), factory.Object, CreateCredentialProvider(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ShouldFail_WhenBlobStorageOptionsInvalid()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var opts = Options.Create(new BlobStorageOptions { ConnectionString = "", AccountEndpoint = "" });
        var act = () => new AzureBlobStorageService(
            opts, CreateDevelopmentEnvironment(), factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());
        act.Should().Throw<InvalidOperationException>().WithMessage("*AccountEndpoint*required*");
    }

    [Fact]
    public void Constructor_ShouldInvokeFactory_WithConnectionStringInDevelopment()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var expectedClient = new BlobServiceClient("UseDevelopmentStorage=true");
        factory.Setup(f => f.Create("UseDevelopmentStorage=true")).Returns(expectedClient);

        var service = new AzureBlobStorageService(
            CreateValidDevelopmentOptions(), CreateDevelopmentEnvironment(), factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());

        factory.Verify(f => f.Create("UseDevelopmentStorage=true"), Times.Once);
        factory.Verify(
            f => f.Create(It.IsAny<TokenCredential>(), It.IsAny<Uri>()), Times.Never);
    }

    [Fact]
    public void Constructor_ShouldInvokeFactory_WithEndpointAndCredentialInProduction()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var expectedClient = new BlobServiceClient("UseDevelopmentStorage=true");
        var endpoint = new Uri("https://storage.example.blob.core.windows.net");
        factory.Setup(f => f.Create(It.IsAny<TokenCredential>(), endpoint)).Returns(expectedClient);

        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Production");
        var opts = Options.Create(new BlobStorageOptions { AccountEndpoint = "https://storage.example.blob.core.windows.net" });

        var service = new AzureBlobStorageService(
            opts, env.Object, factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());

        factory.Verify(
            f => f.Create(It.IsAny<TokenCredential>(), endpoint), Times.Once);
        factory.Verify(f => f.Create(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Constructor_ShouldRejectConnectionString_OutsideDevelopment()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Production");

        var act = () => new AzureBlobStorageService(
            CreateValidDevelopmentOptions(), env.Object, factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionString*only in Development*");
    }

    // ─── Factory error handling ───────────────────────────────────────

    [Fact]
    public void Constructor_ShouldPropagateException_WhenFactoryCreateWithConnectionStringThrows()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Throws(new InvalidOperationException("Factory failure"));

        var act = () => new AzureBlobStorageService(
            CreateValidDevelopmentOptions(), CreateDevelopmentEnvironment(), factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());

        act.Should().Throw<InvalidOperationException>().WithMessage("Factory failure");
    }

    [Fact]
    public void Constructor_ShouldPropagateException_WhenFactoryCreateWithCredentialThrows()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<TokenCredential>(), It.IsAny<Uri>()))
            .Throws(new InvalidOperationException("Factory failure"));

        var act = () => new AzureBlobStorageService(
            CreateValidProductionOptions(), CreateProductionEnvironment(), factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());

        act.Should().Throw<InvalidOperationException>().WithMessage("Factory failure");
    }

    // ─── UploadAsync argument validation ──────────────────────────────

    [Fact]
    public async Task UploadAsync_ShouldThrowArgumentNullException_WhenContentIsNull()
    {
        var sut = CreateService();

        var act = () => sut.UploadAsync("container", "blob", null!, "application/pdf");

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("content");
    }

    [Fact]
    public async Task UploadAsync_ShouldThrowArgumentException_WhenContainerNameIsEmpty()
    {
        var sut = CreateService();
        var content = new MemoryStream([1, 2, 3]);

        var act = () => sut.UploadAsync("", "blob", content, "application/pdf");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("containerName");
    }

    [Fact]
    public async Task UploadAsync_ShouldThrowArgumentException_WhenBlobNameIsEmpty()
    {
        var sut = CreateService();
        var content = new MemoryStream([1, 2, 3]);

        var act = () => sut.UploadAsync("container", "", content, "application/pdf");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("blobName");
    }

    // ─── ExistsAsync argument validation ──────────────────────────────

    [Fact]
    public async Task ExistsAsync_ShouldThrowArgumentException_WhenContainerNameIsEmpty()
    {
        var sut = CreateService();

        var act = () => sut.ExistsAsync("", "blob");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("containerName");
    }

    [Fact]
    public async Task ExistsAsync_ShouldThrowArgumentException_WhenBlobNameIsEmpty()
    {
        var sut = CreateService();

        var act = () => sut.ExistsAsync("container", "");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("blobName");
    }

    // ─── ListAsync argument validation ────────────────────────────────

    [Fact]
    public async Task ListAsync_ShouldThrowArgumentException_WhenContainerNameIsEmpty()
    {
        var sut = CreateService();

        var act = () => sut.ListAsync("");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("containerName");
    }

    // ─── DownloadAsync argument validation ────────────────────────────

    [Fact]
    public async Task DownloadAsync_ShouldThrowArgumentException_WhenContainerNameIsEmpty()
    {
        var sut = CreateService();

        var act = () => sut.DownloadAsync("", "blob");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("containerName");
    }

    [Fact]
    public async Task DownloadAsync_ShouldThrowArgumentException_WhenBlobNameIsEmpty()
    {
        var sut = CreateService();

        var act = () => sut.DownloadAsync("container", "");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("blobName");
    }

    // ─── DeleteIfExistsAsync argument validation ──────────────────────

    [Fact]
    public async Task DeleteIfExistsAsync_ShouldThrowArgumentException_WhenContainerNameIsEmpty()
    {
        var sut = CreateService();

        var act = () => sut.DeleteIfExistsAsync("", "blob");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("containerName");
    }

    [Fact]
    public async Task DeleteIfExistsAsync_ShouldThrowArgumentException_WhenBlobNameIsEmpty()
    {
        var sut = CreateService();

        var act = () => sut.DeleteIfExistsAsync("container", "");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("blobName");
    }

    // ─── SetMetadataAsync argument validation ─────────────────────────

    [Fact]
    public async Task SetMetadataAsync_ShouldThrowArgumentNullException_WhenMetadataIsNull()
    {
        var sut = CreateService();

        var act = () => sut.SetMetadataAsync("container", "blob", null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("metadata");
    }

    [Fact]
    public async Task SetMetadataAsync_ShouldThrowArgumentException_WhenContainerNameIsEmpty()
    {
        var sut = CreateService();

        var act = () => sut.SetMetadataAsync("", "blob", new Dictionary<string, string>());

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("containerName");
    }

    [Fact]
    public async Task SetMetadataAsync_ShouldThrowArgumentException_WhenBlobNameIsEmpty()
    {
        var sut = CreateService();

        var act = () => sut.SetMetadataAsync("container", "", new Dictionary<string, string>());

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("blobName");
    }

    // ─── Blob client mocking helpers ──────────────────────────────────

    private static (Mock<BlobServiceClient> service, Mock<BlobContainerClient> container, Mock<BlobClient> blob) CreateMockBlobChain(string connectionString = "UseDevelopmentStorage=true")
    {
        var service = new Mock<BlobServiceClient>(connectionString);
        var container = new Mock<BlobContainerClient>("UseDevelopmentStorage=true", "test-container");
        var blob = new Mock<BlobClient>("UseDevelopmentStorage=true", "test-container", "test-blob");

        service.Setup(s => s.GetBlobContainerClient("test-container")).Returns(container.Object);
        container.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(blob.Object);
        blob.SetupGet(b => b.Uri).Returns(new Uri("https://storage.example.com/test-container/test-blob"));

        return (service, container, blob);
    }

    private static void SetupBlobUpload(Mock<BlobClient> blob)
        => blob.Setup(b => b.UploadAsync(
            It.IsAny<Stream>(), 
            It.IsAny<BlobUploadOptions>(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContentInfo>(), Mock.Of<Response>()));

    private static void SetupBlobExists(Mock<BlobClient> blob, bool exists)
        => blob.Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(exists, Mock.Of<Response>()));

    private static void SetupBlobGetProperties(Mock<BlobClient> blob)
        => blob.Setup(b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobProperties>(), Mock.Of<Response>()));

    private static void SetupBlobDownload(Mock<BlobClient> blob)
        => blob.Setup(b => b.DownloadStreamingAsync(
            It.IsAny<HttpRange>(), 
            It.IsAny<BlobRequestConditions>(), 
            It.IsAny<bool>(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobDownloadStreamingResult>(), Mock.Of<Response>()));

    private static void SetupBlobDelete(Mock<BlobClient> blob, bool exists)
        => blob.Setup(b => b.DeleteIfExistsAsync(
            It.IsAny<DeleteSnapshotsOption>(), 
            It.IsAny<BlobRequestConditions>(), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(exists, Mock.Of<Response>()));

    private static void SetupBlobSetMetadata(Mock<BlobClient> blob, Dictionary<string, string> metadata)
        => blob.Setup(b => b.SetMetadataAsync(metadata, It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobInfo>(), Mock.Of<Response>()));

    // ─── Factory invocation for production ────────────────────────────

    [Fact]
    public void Constructor_ShouldSucceed_WithValidProductionOptions()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var expectedClient = new BlobServiceClient("UseDevelopmentStorage=true");
        var endpoint = new Uri("https://storage.example.blob.core.windows.net");
        factory.Setup(f => f.Create(It.IsAny<TokenCredential>(), endpoint)).Returns(expectedClient);

        var act = () => new AzureBlobStorageService(
            CreateValidProductionOptions(), CreateProductionEnvironment(), factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());

        act.Should().NotThrow();
    }

    // ─── ListAsync ────────────────────────────────────────────────────

    private static AzureBlobStorageService CreateServiceWithMockBlobChain(
        Mock<BlobServiceClient> serviceMock,
        IOptions<BlobStorageOptions>? options = null)
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(serviceMock.Object);
        factory.Setup(f => f.Create(It.IsAny<TokenCredential>(), It.IsAny<Uri>())).Returns(serviceMock.Object);

        return new AzureBlobStorageService(
            options ?? CreateValidDevelopmentOptions(),
            CreateDevelopmentEnvironment(),
            factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());
    }

    /// <summary>
    /// Minimal fake <see cref="AsyncPageable{T}"/> for unit-testing
    /// <c>await foreach</c> over <see cref="BlobItem"/> collections.
    /// Only <see cref="GetAsyncEnumerator"/> is used by <c>await foreach</c>;
    /// the <see cref="AsPages"/> path is not exercised by the current tests.
    /// </summary>
    private sealed class FakeBlobItemPageable : AsyncPageable<BlobItem>
    {
        private readonly BlobItem[] _items;

        public FakeBlobItemPageable(BlobItem[] items) => _items = items;

        public override async IAsyncEnumerator<BlobItem> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            foreach (var item in _items)
                yield return item;
        }

        public override AsyncPageable<Page<BlobItem>> AsPages(
            string? continuationToken = null, int? pageSizeHint = null)
            => throw new NotImplementedException("AsPages is not exercised by current tests");
    }

    private static BlobItem CreateBlobItemStub(string name, string contentType, long contentLength)
    {
        var props = BlobsModelFactory.BlobItemProperties(
            accessTierInferred: false,
            contentType: contentType,
            contentLength: contentLength);
        return BlobsModelFactory.BlobItem(name, deleted: false, properties: props, versionId: null, metadata: null);
    }

    [Fact]
    public async Task ListAsync_WithBlobs_ShouldReturnDescriptors()
    {
        var (service, container, _) = CreateMockBlobChain();
        var blobItems = new[]
        {
            CreateBlobItemStub("blob1.pdf", "application/pdf", 1024L),
            CreateBlobItemStub("blob2.txt", "text/plain", 512L)
        };
        var pageable = new FakeBlobItemPageable(blobItems);
        container.Setup(c => c.GetBlobsAsync(It.IsAny<GetBlobsOptions>(), It.IsAny<CancellationToken>()))
            .Returns(pageable);

        var sut = CreateServiceWithMockBlobChain(service);

        var result = await sut.ListAsync("test-container");

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("blob1.pdf");
        result[0].ContentType.Should().Be("application/pdf");
        result[0].SizeBytes.Should().Be(1024L);
        result[1].Name.Should().Be("blob2.txt");
        result[1].ContentType.Should().Be("text/plain");
        result[1].SizeBytes.Should().Be(512L);
    }

    [Fact]
    public async Task ListAsync_WithEmptyContainer_ShouldReturnEmptyList()
    {
        var (service, container, _) = CreateMockBlobChain();
        var pageable = new FakeBlobItemPageable(Array.Empty<BlobItem>());
        container.Setup(c => c.GetBlobsAsync(It.IsAny<GetBlobsOptions>(), It.IsAny<CancellationToken>()))
            .Returns(pageable);

        var sut = CreateServiceWithMockBlobChain(service);

        var result = await sut.ListAsync("test-container");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_WhenContainerNotFound_ShouldReturnEmptyArray()
    {
        var (service, container, _) = CreateMockBlobChain();
        container.Setup(c => c.GetBlobsAsync(It.IsAny<GetBlobsOptions>(), It.IsAny<CancellationToken>()))
            .Throws(new RequestFailedException(404, "ContainerNotFound"));

        var sut = CreateServiceWithMockBlobChain(service);

        var result = await sut.ListAsync("test-container");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_WhenUnhandledError_ShouldPropagateException()
    {
        var (service, container, _) = CreateMockBlobChain();
        container.Setup(c => c.GetBlobsAsync(It.IsAny<GetBlobsOptions>(), It.IsAny<CancellationToken>()))
            .Throws(new RequestFailedException(500, "InternalError"));

        var sut = CreateServiceWithMockBlobChain(service);

        var act = async () => await sut.ListAsync("test-container");

        await act.Should().ThrowAsync<RequestFailedException>()
            .Where(ex => ex.Status == 500);
    }

    [Fact]
    public async Task ListAsync_ShouldThrowArgumentException_WhenContainerNameIsNull()
    {
        var sut = CreateService();

        var act = () => sut.ListAsync(null!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("containerName");
    }

    [Fact]
    public async Task ListAsync_WithCancellationToken_ShouldPassToken()
    {
        var (service, container, _) = CreateMockBlobChain();
        var pageable = new FakeBlobItemPageable(Array.Empty<BlobItem>());
        container.Setup(c => c.GetBlobsAsync(It.IsAny<GetBlobsOptions>(), It.IsAny<CancellationToken>()))
            .Returns(pageable);

        var sut = CreateServiceWithMockBlobChain(service);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await sut.ListAsync("test-container", cts.Token);

        result.Should().BeEmpty();
        container.Verify(
            c => c.GetBlobsAsync(It.IsAny<GetBlobsOptions>(), cts.Token),
            Times.Once);
    }

    // ─── UploadAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UploadAsync_ShouldReturnBlobUri()
    {
        var (service, container, blob) = CreateMockBlobChain();
        container.Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContainerInfo>(), Mock.Of<Response>()));
        container.Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContainerInfo>(), Mock.Of<Response>()));
        SetupBlobUpload(blob);

        var sut = CreateServiceWithMockBlobChain(service);
        using var content = new MemoryStream([1, 2, 3]);

        var result = await sut.UploadAsync("test-container", "test-blob", content, "application/pdf");

        result.Should().NotBeNull();
        result.Should().Contain("test-container");
        result.Should().Contain("test-blob");
    }

    [Fact]
    public async Task UploadAsync_WhenContainerCreateFails_ShouldPropagateException()
    {
        var (service, container, _) = CreateMockBlobChain();
        // Set up all possible overloads - the SDK may resolve to any of them
        container.Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "Forbidden"));
        container.Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "Forbidden"));

        var sut = CreateServiceWithMockBlobChain(service);
        using var content = new MemoryStream([1, 2, 3]);

        var act = async () => await sut.UploadAsync("test-container", "test-blob", content, "application/pdf");

        await act.Should().ThrowAsync<RequestFailedException>()
            .Where(ex => ex.Status == 403);
    }

    [Fact]
    public async Task UploadAsync_WhenUploadFails_ShouldPropagateException()
    {
        var (service, container, blob) = CreateMockBlobChain();
        container.Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContainerInfo>(), Mock.Of<Response>()));
        container.Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContainerInfo>(), Mock.Of<Response>()));
        blob.Setup(b => b.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<BlobUploadOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(409, "Conflict"));

        var sut = CreateServiceWithMockBlobChain(service);
        using var content = new MemoryStream([1, 2, 3]);

        var act = async () => await sut.UploadAsync("test-container", "test-blob", content, "application/pdf");

        await act.Should().ThrowAsync<RequestFailedException>()
            .Where(ex => ex.Status == 409);
    }

    [Fact]
    public async Task UploadAsync_ShouldThrowArgumentException_WhenContainerNameIsNull()
    {
        var sut = CreateService();
        using var content = new MemoryStream([1, 2, 3]);

#pragma warning disable CA2025 // content lifetime exceeds awaiting task — test verifies argument validation only, no I/O
        var act = () => sut.UploadAsync(null!, "blob", content, "application/pdf");
#pragma warning restore CA2025

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("containerName");
    }

    [Fact]
    public async Task UploadAsync_ShouldThrowArgumentException_WhenBlobNameIsNull()
    {
        var sut = CreateService();
        using var content = new MemoryStream([1, 2, 3]);

#pragma warning disable CA2025 // content lifetime exceeds awaiting task — test verifies argument validation only, no I/O
        var act = () => sut.UploadAsync("container", null!, content, "application/pdf");
#pragma warning restore CA2025

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("blobName");
    }

    [Fact]
    public async Task UploadAsync_WithCancellationToken_ShouldPassToken()
    {
        var (service, container, blob) = CreateMockBlobChain();
        container.Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContainerInfo>(), Mock.Of<Response>()));
        container.Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContainerInfo>(), Mock.Of<Response>()));
        SetupBlobUpload(blob);

        var sut = CreateServiceWithMockBlobChain(service);
        using var content = new MemoryStream([1, 2, 3]);
        using var cts = new CancellationTokenSource();

        await sut.UploadAsync("test-container", "test-blob", content, "application/pdf", cts.Token);

        container.Verify(
            c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<Dictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                cts.Token),
            Times.AtLeastOnce);
    }
}
