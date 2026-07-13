using Azure.Core;
using Azure.Storage.Blobs;
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

    private static IBlobServiceClientFactory CreateFactoryReturningValidClient()
    {
        var factory = new Mock<IBlobServiceClientFactory>();
        var serviceClient = new BlobServiceClient("UseDevelopmentStorage=true");
        factory.Setup(f => f.Create(It.IsAny<string>())).Returns(serviceClient);
        factory.Setup(f => f.Create(It.IsAny<TokenCredential>(), It.IsAny<Uri>()))
            .Returns(serviceClient);
        return factory.Object;
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsIsNull()
    {
        var env = CreateDevelopmentEnvironment();
        var act = () => new BlobManualPageAssetStore(
            null!, env, CreateFactoryReturningValidClient(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenEnvironmentIsNull()
    {
        var act = () => new BlobManualPageAssetStore(
            CreateValidDevOptions(), null!, CreateFactoryReturningValidClient(),
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("environment");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenFactoryIsNull()
    {
        var act = () => new BlobManualPageAssetStore(
            CreateValidDevOptions(), CreateDevelopmentEnvironment(), null!,
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("blobServiceClientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var act = () => new BlobManualPageAssetStore(
            CreateValidDevOptions(), CreateDevelopmentEnvironment(), CreateFactoryReturningValidClient(), null!);
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
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());
        act.Should().Throw<InvalidOperationException>().WithMessage("*AccountEndpoint*required*");
    }

    [Fact]
    public void Constructor_ShouldSucceed_WithValidDevelopmentOptions()
    {
        var act = () => new BlobManualPageAssetStore(
            CreateValidDevOptions(), CreateDevelopmentEnvironment(), CreateFactoryReturningValidClient(),
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
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());
        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionString*only in Development*");
    }

    [Fact]
    public void UploadPageAsync_ShouldThrowArgumentNullException_WhenContentIsNull()
    {
        var store = new BlobManualPageAssetStore(
            CreateValidDevOptions(), CreateDevelopmentEnvironment(), CreateFactoryReturningValidClient(),
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
            TestHelpers.CreateNullLogger<BlobManualPageAssetStore>());

        act.Should().NotThrow();
    }
}
