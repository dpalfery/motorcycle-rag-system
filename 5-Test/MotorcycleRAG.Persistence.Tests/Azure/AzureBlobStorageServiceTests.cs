using Azure.Core;
using Azure.Storage.Blobs;
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

    private static AzureBlobStorageService CreateService(
        IOptions<BlobStorageOptions>? options = null,
        IHostEnvironment? environment = null,
        IBlobServiceClientFactory? factory = null)
    {
        return new AzureBlobStorageService(
            options ?? CreateValidDevelopmentOptions(),
            environment ?? CreateDevelopmentEnvironment(),
            factory ?? CreateFactoryReturningValidClient().Object,
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsIsNull()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var act = () => new AzureBlobStorageService(
            null!, CreateDevelopmentEnvironment(), factory.Object,
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenEnvironmentIsNull()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var opts = Options.Create(new BlobStorageOptions { AccountEndpoint = "https://mystorage.blob.core.windows.net" });
        var act = () => new AzureBlobStorageService(
            opts, null!, factory.Object, TestHelpers.CreateNullLogger<AzureBlobStorageService>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("environment");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenFactoryIsNull()
    {
        var opts = CreateValidDevelopmentOptions();
        var act = () => new AzureBlobStorageService(
            opts, CreateDevelopmentEnvironment(), null!,
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("blobServiceClientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var opts = CreateValidDevelopmentOptions();
        var act = () => new AzureBlobStorageService(
            opts, CreateDevelopmentEnvironment(), factory.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ShouldFail_WhenBlobStorageOptionsInvalid()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var opts = Options.Create(new BlobStorageOptions { ConnectionString = "", AccountEndpoint = "" });
        var act = () => new AzureBlobStorageService(
            opts, CreateDevelopmentEnvironment(), factory.Object,
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
            TestHelpers.CreateNullLogger<AzureBlobStorageService>());

        act.Should().NotThrow();
    }
}
