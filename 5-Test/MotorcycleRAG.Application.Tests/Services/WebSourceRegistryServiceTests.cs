using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Services;

public sealed class WebSourceRegistryServiceTests
{
    [Fact]
    public async Task AddWebSourceAsync_WhenAuthorizedAndUrlIsNew_CreatesAndReturnsSource()
    {
        var repository = CreateRepository();
        var user = CreateAuthorizedUser();
        var source = CreateSource();
        repository.Setup(repo => repo.GetWebSourceByUrlAsync(It.Is<Uri>(uri => uri.AbsoluteUri == source.Url)))
            .ReturnsAsync((WebSource?)null);
        repository.Setup(repo => repo.CreateWebSourceAsync(source)).ReturnsAsync(source);
        var sut = CreateSut(repository.Object, user.Object);

        var result = await sut.AddWebSourceAsync(source);

        result.Should().BeSameAs(source);
        repository.Verify(repo => repo.CreateWebSourceAsync(source), Times.Once);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AddWebSourceAsync_WhenUserIsNotAuthorized_ThrowsUnauthorizedAccessException(
        bool authenticated,
        bool admin)
    {
        var user = CreateAuthorizedUser(authenticated, admin);
        var sut = CreateSut(CreateRepository().Object, user.Object);

        var act = () => sut.AddWebSourceAsync(CreateSource());

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task AddWebSourceAsync_WhenSourceIsNullEmptyOrDuplicate_Throws()
    {
        var repository = CreateRepository();
        var user = CreateAuthorizedUser();
        var duplicate = CreateSource();
        repository.Setup(repo => repo.GetWebSourceByUrlAsync(It.IsAny<Uri>())).ReturnsAsync(duplicate);
        var sut = CreateSut(repository.Object, user.Object);

        var nullSource = () => sut.AddWebSourceAsync(null!);
        var emptyUrl = () => sut.AddWebSourceAsync(CreateSource(url: " "));
        var duplicateUrl = () => sut.AddWebSourceAsync(duplicate);

        await nullSource.Should().ThrowAsync<ArgumentNullException>();
        await emptyUrl.Should().ThrowAsync<InvalidOperationException>();
        await duplicateUrl.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateWebSourceAsync_WhenAuthorizedAndRepositoryUpdates_ReturnsSourceWithUpdatedTimestamp()
    {
        var repository = CreateRepository();
        var user = CreateAuthorizedUser();
        var source = CreateSource(id: 42);
        repository.Setup(repo => repo.GetWebSourceByIdAsync(source.Id)).ReturnsAsync(CreateSource(id: source.Id));
        repository.Setup(repo => repo.UpdateWebSourceAsync(source)).ReturnsAsync(true);
        var sut = CreateSut(repository.Object, user.Object);

        var result = await sut.UpdateWebSourceAsync(source);

        result.Should().BeSameAs(source);
        source.LastUpdatedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    public async Task UpdateWebSourceAsync_WhenInputOrUserIsInvalid_Throws(
        int sourceId,
        bool authenticated,
        bool admin)
    {
        var sut = CreateSut(CreateRepository().Object, CreateAuthorizedUser(authenticated, admin).Object);

        var act = () => sut.UpdateWebSourceAsync(CreateSource(id: sourceId));

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task UpdateWebSourceAsync_WhenSourceDoesNotExistOrRepositoryRejectsUpdate_Throws()
    {
        var repository = CreateRepository();
        var user = CreateAuthorizedUser();
        repository.Setup(repo => repo.GetWebSourceByIdAsync(1)).ReturnsAsync((WebSource?)null);
        repository.Setup(repo => repo.GetWebSourceByIdAsync(2)).ReturnsAsync(CreateSource(id: 2));
        repository.Setup(repo => repo.UpdateWebSourceAsync(It.Is<WebSource>(source => source.Id == 2))).ReturnsAsync(false);
        var sut = CreateSut(repository.Object, user.Object);

        var missing = () => sut.UpdateWebSourceAsync(CreateSource(id: 1));
        var rejected = () => sut.UpdateWebSourceAsync(CreateSource(id: 2));

        await missing.Should().ThrowAsync<InvalidOperationException>();
        await rejected.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RemoveWebSourceAsync_WhenAuthorizedAndRepositoryDeletes_Completes()
    {
        var repository = CreateRepository();
        var user = CreateAuthorizedUser();
        repository.Setup(repo => repo.GetWebSourceByIdAsync(1)).ReturnsAsync(CreateSource(id: 1));
        repository.Setup(repo => repo.DeleteWebSourceAsync(1)).ReturnsAsync(true);
        var sut = CreateSut(repository.Object, user.Object);

        await sut.RemoveWebSourceAsync(1);

        repository.Verify(repo => repo.DeleteWebSourceAsync(1), Times.Once);
    }

    [Theory]
    [InlineData(0, true, true)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    public async Task RemoveWebSourceAsync_WhenInputOrUserIsInvalid_Throws(
        int sourceId,
        bool authenticated,
        bool admin)
    {
        var sut = CreateSut(CreateRepository().Object, CreateAuthorizedUser(authenticated, admin).Object);

        var act = () => sut.RemoveWebSourceAsync(sourceId);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task RemoveWebSourceAsync_WhenSourceDoesNotExistOrRepositoryRejectsDelete_Throws()
    {
        var repository = CreateRepository();
        var user = CreateAuthorizedUser();
        repository.Setup(repo => repo.GetWebSourceByIdAsync(1)).ReturnsAsync((WebSource?)null);
        repository.Setup(repo => repo.GetWebSourceByIdAsync(2)).ReturnsAsync(CreateSource(id: 2));
        repository.Setup(repo => repo.DeleteWebSourceAsync(2)).ReturnsAsync(false);
        var sut = CreateSut(repository.Object, user.Object);

        var missing = () => sut.RemoveWebSourceAsync(1);
        var rejected = () => sut.RemoveWebSourceAsync(2);

        await missing.Should().ThrowAsync<InvalidOperationException>();
        await rejected.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SourceQueries_ReturnRepositoryValuesAndFilterEnabledSearchSources()
    {
        var repository = CreateRepository();
        var sources = new[]
        {
            CreateSource(id: 1, enabled: true, includeInSearch: true),
            CreateSource(id: 2, enabled: false, includeInSearch: true),
            CreateSource(id: 3, enabled: true, includeInSearch: false),
        };
        repository.Setup(repo => repo.GetAllWebSourcesAsync()).ReturnsAsync(sources);
        repository.Setup(repo => repo.GetWebSourceByIdAsync(1)).ReturnsAsync(sources[0]);
        var sut = CreateSut(repository.Object, CreateAuthorizedUser().Object);

        var all = await sut.GetAllSourcesAsync();
        var enabled = await sut.GetEnabledSourcesAsync();
        var source = await sut.GetSourceByIdAsync(1);

        all.Should().BeSameAs(sources);
        enabled.Should().ContainSingle().Which.Should().BeSameAs(sources[0]);
        source.Should().BeSameAs(sources[0]);
    }

    [Fact]
    public async Task UpdateCrawlDateAsync_WhenSourceExists_UpdatesDateAndSavesSource()
    {
        var repository = CreateRepository();
        var source = CreateSource(id: 7);
        repository.Setup(repo => repo.GetWebSourceByIdAsync(7)).ReturnsAsync(source);
        repository.Setup(repo => repo.UpdateWebSourceAsync(source)).ReturnsAsync(true);
        var sut = CreateSut(repository.Object, CreateAuthorizedUser().Object);

        await sut.UpdateCrawlDateAsync(7);

        source.LastCrawledDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        repository.Verify(repo => repo.UpdateWebSourceAsync(source), Times.Once);
    }

    [Fact]
    public async Task UpdateCrawlDateAsync_WhenSourceDoesNotExist_ThrowsInvalidOperationException()
    {
        var repository = CreateRepository();
        repository.Setup(repo => repo.GetWebSourceByIdAsync(7)).ReturnsAsync((WebSource?)null);
        var sut = CreateSut(repository.Object, CreateAuthorizedUser().Object);

        var act = () => sut.UpdateCrawlDateAsync(7);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_WhenDependencyIsNull_ThrowsArgumentNullException()
    {
        var repository = CreateRepository().Object;
        var user = CreateAuthorizedUser().Object;

        ((Action)(() => new WebSourceRegistryService(null!, user, NullLogger<WebSourceRegistryService>.Instance)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new WebSourceRegistryService(repository, null!, NullLogger<WebSourceRegistryService>.Instance)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new WebSourceRegistryService(repository, user, null!)))
            .Should().Throw<ArgumentNullException>();
    }

    private static Mock<IWebSourceRepository> CreateRepository() => new(MockBehavior.Strict);

    private static Mock<ICurrentUserService> CreateAuthorizedUser(
        bool authenticated = true,
        bool admin = true)
    {
        var user = new Mock<ICurrentUserService>(MockBehavior.Strict);
        user.SetupGet(currentUser => currentUser.IsAuthenticated).Returns(authenticated);
        user.Setup(currentUser => currentUser.IsInRole("mcr-api-admin")).Returns(admin);
        user.SetupGet(currentUser => currentUser.UserId).Returns("admin-id");
        return user;
    }

    private static WebSourceRegistryService CreateSut(
        IWebSourceRepository repository,
        ICurrentUserService user) => new(
        repository,
        user,
        NullLogger<WebSourceRegistryService>.Instance);

    private static WebSource CreateSource(
        int id = 1,
        string url = "https://example.test/source",
        bool enabled = true,
        bool includeInSearch = true) => new()
    {
        Id = id,
        Url = url,
        Name = "Unit source",
        IsEnabled = enabled,
        IncludeInSearch = includeInSearch,
    };
}
