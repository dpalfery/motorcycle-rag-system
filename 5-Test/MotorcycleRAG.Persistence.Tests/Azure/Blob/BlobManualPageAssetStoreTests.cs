using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Azure;
using MotorcycleRAG.Persistence.Azure.Blob;

namespace MotorcycleRAG.Persistence.Tests.Azure.Blob;

public class BlobManualPageAssetStoreTests
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

    private static IOptions<BlobStorageOptions> CreateValidDevOptions()
        => Options.Create(new BlobStorageOptions
        {
            ConnectionString = "UseDevelopmentStorage=true"
        });

    private static IOptions<BlobStorageOptions> CreateValidProdOptions()
        => Options.Create(new BlobStorageOptions
        {
            AccountEndpoint = "https://storage.example.blob.core.windows.net"
        });

    private static Mock<IBlobServiceClientFactory> CreateFactoryReturningMockServiceClient(
        Mock<BlobServiceClient> serviceClientMock)
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(serviceClientMock.Object);
        factory.Setup(f => f.Create(It.IsAny<TokenCredential>(), It.IsAny<Uri>()))
            .Returns(serviceClientMock.Object);
        return factory;
    }

    private static IBlobServiceClientFactory CreateFactoryReturningValidClient()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var serviceClient = new BlobServiceClient("UseDevelopmentStorage=true");
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(serviceClient);
        factory.Setup(f => f.Create(It.IsAny<TokenCredential>(), It.IsAny<Uri>()))
            .Returns(serviceClient);
        return factory.Object;
    }

    private static IAzureCredentialProvider CreateCredentialProvider()
    {
        var provider = new Mock<IAzureCredentialProvider>();
        provider.Setup(x => x.GetDefaultCredential()).Returns(new global::Azure.Identity.DefaultAzureCredential());
        return provider.Object;
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsIsNull()
    {
        var env = CreateDevelopmentEnvironment();
        var act = () => new BlobManualPageAssetStore(
            null!, env, CreateFactoryReturningValidClient(),
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenEnvironmentIsNull()
    {
        var act = () => new BlobManualPageAssetStore(
            CreateValidDevOptions(), null!, CreateFactoryReturningValidClient(),
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("environment");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenFactoryIsNull()
    {
        var act = () => new BlobManualPageAssetStore(
            CreateValidDevOptions(), CreateDevelopmentEnvironment(), null!,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("blobServiceClientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenCredentialProviderIsNull()
    {
        var act = () => new BlobManualPageAssetStore(
            CreateValidDevOptions(), CreateDevelopmentEnvironment(),
            CreateFactoryReturningValidClient(), null!,
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("credentialProvider");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var act = () => new BlobManualPageAssetStore(
            CreateValidDevOptions(), CreateDevelopmentEnvironment(), CreateFactoryReturningValidClient(), CreateCredentialProvider(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ShouldThrowInvalidOperationException_WhenOptionsAreInvalid()
    {
        var opts = Options.Create(new BlobStorageOptions
        {
            ConnectionString = "",
            AccountEndpoint = ""
        });
        var env = CreateDevelopmentEnvironment();
        var act = () => new BlobManualPageAssetStore(
            opts, env, CreateFactoryReturningValidClient(),
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());
        act.Should().Throw<InvalidOperationException>().WithMessage("*AccountEndpoint*required*");
    }

    [Fact]
    public void Constructor_ShouldSucceed_WithValidDevelopmentOptions()
    {
        var act = () => new BlobManualPageAssetStore(
            CreateValidDevOptions(), CreateDevelopmentEnvironment(), CreateFactoryReturningValidClient(),
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenConnectionStringUsedInNonDevelopment()
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns("Production");
        var act = () => new BlobManualPageAssetStore(
            CreateValidDevOptions(), env.Object, CreateFactoryReturningValidClient(),
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());
        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionString*only in Development*");
    }

    [Fact]
    public void UploadPageAsync_ShouldThrowArgumentNullException_WhenContentIsNull()
    {
        var store = new BlobManualPageAssetStore(
            CreateValidDevOptions(), CreateDevelopmentEnvironment(), CreateFactoryReturningValidClient(),
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());
        var act = () => store.UploadPageAsync(Guid.NewGuid(), 1, null!);
        act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("content");
    }

    [Fact]
    public void Constructor_ShouldInvokeFactory_WithConnectionStringInDevelopment()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var serviceClient = new BlobServiceClient("UseDevelopmentStorage=true");
        factory.Setup(f => f.Create("UseDevelopmentStorage=true")).Returns(serviceClient);

        var store = new BlobManualPageAssetStore(
            CreateValidDevOptions(), CreateDevelopmentEnvironment(), factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());

        factory.Verify(f => f.Create("UseDevelopmentStorage=true"), Times.Once);
    }

    // ─── Factory error handling ───────────────────────────────────────

    [Fact]
    public void Constructor_ShouldPropagateException_WhenFactoryCreateWithConnectionStringThrows()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<string>()))
            .Throws(new InvalidOperationException("Factory failure"));

        var act = () => new BlobManualPageAssetStore(
            CreateValidDevOptions(), CreateDevelopmentEnvironment(), factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());

        act.Should().Throw<InvalidOperationException>().WithMessage("Factory failure");
    }

    [Fact]
    public void Constructor_ShouldPropagateException_WhenFactoryCreateWithCredentialThrows()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        factory.Setup(f => f.Create(It.IsAny<TokenCredential>(), It.IsAny<Uri>()))
            .Throws(new InvalidOperationException("Factory failure"));

        var act = () => new BlobManualPageAssetStore(
            CreateValidProdOptions(), CreateProductionEnvironment(), factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());

        act.Should().Throw<InvalidOperationException>().WithMessage("Factory failure");
    }

    // ─── Production environment factory delegation ────────────────────

    [Fact]
    public void Constructor_ShouldInvokeFactory_WithEndpointAndCredentialInProduction()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var expectedClient = new BlobServiceClient("UseDevelopmentStorage=true");
        var endpoint = new Uri("https://storage.example.blob.core.windows.net");
        factory.Setup(f => f.Create(It.IsAny<TokenCredential>(), endpoint)).Returns(expectedClient);

        var store = new BlobManualPageAssetStore(
            CreateValidProdOptions(), CreateProductionEnvironment(), factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());

        store.Should().NotBeNull();
        factory.Verify(
            f => f.Create(It.IsAny<TokenCredential>(), endpoint), Times.Once);
        factory.Verify(f => f.Create(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Constructor_ShouldSucceed_WithValidProductionOptions()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var expectedClient = new BlobServiceClient("UseDevelopmentStorage=true");
        factory.Setup(f => f.Create(It.IsAny<TokenCredential>(), It.IsAny<Uri>())).Returns(expectedClient);

        var act = () => new BlobManualPageAssetStore(
            CreateValidProdOptions(), CreateProductionEnvironment(), factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());

        act.Should().NotThrow();
    }

    // ─── Blob mock chain helper ──────────────────────────────────────

    private static (Mock<BlobServiceClient> Service, Mock<BlobContainerClient> Container, Mock<BlobClient> Blob)
        CreateMockBlobChain(string containerName = "manual-pages")
    {
        var service = new Mock<BlobServiceClient>("UseDevelopmentStorage=true");
        var container = new Mock<BlobContainerClient>("UseDevelopmentStorage=true", containerName);
        var blob = new Mock<BlobClient>("UseDevelopmentStorage=true", containerName, "test-blob");

        service.Setup(s => s.GetBlobContainerClient(containerName)).Returns(container.Object);
        container.Setup(c => c.GetBlobClient(It.IsAny<string>())).Returns(blob.Object);

        return (service, container, blob);
    }

    private static BlobManualPageAssetStore CreateStoreWithMockBlobChain(
        Mock<BlobServiceClient> serviceMock,
        IOptions<BlobStorageOptions>? options = null)
    {
        var factory = CreateFactoryReturningMockServiceClient(serviceMock);
        return new BlobManualPageAssetStore(
            options ?? CreateValidDevOptions(),
            CreateDevelopmentEnvironment(),
            factory.Object,
            CreateCredentialProvider(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());
    }

    // ─── DownloadPageAsync ────────────────────────────────────────────

    [Fact]
    public async Task DownloadPageAsync_ShouldReturnContentStream()
    {
        var (service, container, blob) = CreateMockBlobChain();
        var contentStream = new MemoryStream([1, 2, 3, 4]);
        var downloadResult = BlobsModelFactory.BlobDownloadStreamingResult(contentStream, new BlobDownloadDetails());
        var azureResponse = Response.FromValue(downloadResult, Mock.Of<Response>());
        blob.Setup(b => b.DownloadStreamingAsync(
                It.IsAny<BlobDownloadOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(azureResponse);
        blob.Setup(b => b.DownloadStreamingAsync(
                It.IsAny<HttpRange>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(azureResponse);

        var store = CreateStoreWithMockBlobChain(service);

        var manualId = Guid.NewGuid();
        var result = await store.DownloadPageAsync(manualId, 1);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DownloadPageAsync_WhenBlobNotFound_ShouldPropagateException()
    {
        var (service, container, blob) = CreateMockBlobChain();
        blob.Setup(b => b.DownloadStreamingAsync(
                It.IsAny<BlobDownloadOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "BlobNotFound"));
        blob.Setup(b => b.DownloadStreamingAsync(
                It.IsAny<HttpRange>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "BlobNotFound"));

        var store = CreateStoreWithMockBlobChain(service);

        var act = async () => await store.DownloadPageAsync(Guid.NewGuid(), 1);

        await act.Should().ThrowAsync<RequestFailedException>()
            .Where(ex => ex.Status == 404);
    }

    // ─── PageExistsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task PageExistsAsync_WhenBlobExists_ShouldReturnTrue()
    {
        var (service, container, blob) = CreateMockBlobChain();
        blob.Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        var store = CreateStoreWithMockBlobChain(service);

        var result = await store.PageExistsAsync(Guid.NewGuid(), 1);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task PageExistsAsync_WhenBlobDoesNotExist_ShouldReturnFalse()
    {
        var (service, container, blob) = CreateMockBlobChain();
        blob.Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(false, Mock.Of<Response>()));

        var store = CreateStoreWithMockBlobChain(service);

        var result = await store.PageExistsAsync(Guid.NewGuid(), 2);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task PageExistsAsync_WhenSdkThrows_ShouldPropagateException()
    {
        var (service, container, blob) = CreateMockBlobChain();
        blob.Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test failure"));

        var store = CreateStoreWithMockBlobChain(service);

        var act = async () => await store.PageExistsAsync(Guid.NewGuid(), 1);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Test failure");
    }

    // ─── GetETagAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetETagAsync_WhenBlobExists_ShouldReturnETag()
    {
        var (service, container, blob) = CreateMockBlobChain();
        var properties = BlobsModelFactory.BlobProperties(eTag: new ETag("\"0x8DABCDEF\""));
        blob.Setup(b => b.GetPropertiesAsync(
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(properties, Mock.Of<Response>()));

        var store = CreateStoreWithMockBlobChain(service);

        var result = await store.GetETagAsync(Guid.NewGuid(), 1);

        result.Should().NotBeNull();
        result.Should().Contain("0x8DABCDEF");
    }

    [Fact]
    public async Task GetETagAsync_WhenBlobNotFound_ShouldReturnNull()
    {
        var (service, container, blob) = CreateMockBlobChain();
        blob.Setup(b => b.GetPropertiesAsync(
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "BlobNotFound",
                BlobErrorCode.BlobNotFound.ToString(), null));

        var store = CreateStoreWithMockBlobChain(service);

        var result = await store.GetETagAsync(Guid.NewGuid(), 1);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetETagAsync_WhenNon404Error_ShouldPropagateException()
    {
        var (service, container, blob) = CreateMockBlobChain();
        blob.Setup(b => b.GetPropertiesAsync(
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, "InternalError"));

        var store = CreateStoreWithMockBlobChain(service);

        var act = async () => await store.GetETagAsync(Guid.NewGuid(), 1);

        await act.Should().ThrowAsync<RequestFailedException>()
            .Where(ex => ex.Status == 500);
    }

    // ─── UploadPageAsync ─────────────────────────────────────────────

    [Fact]
    public async Task UploadPageAsync_ShouldReturnBlobKey()
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
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContentInfo>(), Mock.Of<Response>()));

        var store = CreateStoreWithMockBlobChain(service);
        var manualId = Guid.NewGuid();
        using var content = new MemoryStream([1, 2, 3]);

        var result = await store.UploadPageAsync(manualId, 1, content);

        result.Should().Be($"manuals/{manualId}/pages/1.png");
    }

    [Fact]
    public async Task UploadPageAsync_WhenContainerCreateFails_ShouldPropagateException()
    {
        var (service, container, blob) = CreateMockBlobChain();
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

        var store = CreateStoreWithMockBlobChain(service);
        using var content = new MemoryStream([1, 2, 3]);

        var act = async () => await store.UploadPageAsync(Guid.NewGuid(), 1, content);

        await act.Should().ThrowAsync<RequestFailedException>()
            .Where(ex => ex.Status == 403);
    }

    [Fact]
    public async Task UploadPageAsync_WhenContainerCreateFails_OnAllOverloads_ShouldPropagateException()
    {
        var (service, container, blob) = CreateMockBlobChain();
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

        var store = CreateStoreWithMockBlobChain(service);
        using var content = new MemoryStream([1, 2, 3]);

        var act2 = async () => await store.UploadPageAsync(Guid.NewGuid(), 1, content);

        await act2.Should().ThrowAsync<RequestFailedException>()
            .Where(ex => ex.Status == 403);
    }

    [Fact]
    public async Task UploadPageAsync_WhenUploadFails_ShouldPropagateException()
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

        var store = CreateStoreWithMockBlobChain(service);
        using var content = new MemoryStream([1, 2, 3]);

        var act = async () => await store.UploadPageAsync(Guid.NewGuid(), 1, content);

        await act.Should().ThrowAsync<RequestFailedException>()
            .Where(ex => ex.Status == 409);
    }

    [Fact]
    public async Task UploadPageAsync_WithCancellationToken_ShouldPassToken()
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
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContentInfo>(), Mock.Of<Response>()));

        var store = CreateStoreWithMockBlobChain(service);
        using var content = new MemoryStream([1, 2, 3]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await store.UploadPageAsync(Guid.NewGuid(), 1, content, cts.Token);

        blob.Verify(b => b.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<BlobUploadOptions>(),
                cts.Token),
            Times.Once);
    }

    // ─── Additional coverage tests ────────────────────────────────────

    [Fact]
    public async Task DownloadPageAsync_WithCancellationToken_ShouldPassTokenToSdk()
    {
        var (service, container, blob) = CreateMockBlobChain();
        var contentStream = new MemoryStream([1, 2, 3, 4]);
        var downloadResult = BlobsModelFactory.BlobDownloadStreamingResult(contentStream, new BlobDownloadDetails());
        var azureResponse = Response.FromValue(downloadResult, Mock.Of<Response>());
        blob.Setup(b => b.DownloadStreamingAsync(
                It.IsAny<BlobDownloadOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(azureResponse);
        blob.Setup(b => b.DownloadStreamingAsync(
                It.IsAny<HttpRange>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(azureResponse);

        var store = CreateStoreWithMockBlobChain(service);
        using var cts = new CancellationTokenSource();
        var manualId = Guid.NewGuid();

        await store.DownloadPageAsync(manualId, 1, cts.Token);

        blob.Verify(
            b => b.DownloadStreamingAsync(
                It.IsAny<BlobDownloadOptions>(),
                cts.Token),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task UploadPageAsync_ShouldSetContentTypeToPng()
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
            .ReturnsAsync(Response.FromValue(Mock.Of<BlobContentInfo>(), Mock.Of<Response>()));

        var store = CreateStoreWithMockBlobChain(service);
        using var content = new MemoryStream([1, 2, 3]);

        await store.UploadPageAsync(Guid.NewGuid(), 1, content);

        blob.Verify(b => b.UploadAsync(
                It.IsAny<Stream>(),
                It.Is<BlobUploadOptions>(o => o.HttpHeaders != null && o.HttpHeaders.ContentType == "image/png"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PageExistsAsync_WithCancellationToken_ShouldPassTokenToSdk()
    {
        var (service, container, blob) = CreateMockBlobChain();
        blob.Setup(b => b.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(true, Mock.Of<Response>()));

        var store = CreateStoreWithMockBlobChain(service);
        using var cts = new CancellationTokenSource();

        await store.PageExistsAsync(Guid.NewGuid(), 1, cts.Token);

        blob.Verify(b => b.ExistsAsync(cts.Token), Times.Once);
    }

    [Fact]
    public async Task GetETagAsync_WithCancellationToken_ShouldPassTokenToSdk()
    {
        var (service, container, blob) = CreateMockBlobChain();
        var properties = BlobsModelFactory.BlobProperties(eTag: new ETag("\"0x8DABCDEF\""));
        blob.Setup(b => b.GetPropertiesAsync(
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(properties, Mock.Of<Response>()));

        var store = CreateStoreWithMockBlobChain(service);
        using var cts = new CancellationTokenSource();

        await store.GetETagAsync(Guid.NewGuid(), 1, cts.Token);

        blob.Verify(
            b => b.GetPropertiesAsync(It.IsAny<BlobRequestConditions>(), cts.Token),
            Times.Once);
    }
}
