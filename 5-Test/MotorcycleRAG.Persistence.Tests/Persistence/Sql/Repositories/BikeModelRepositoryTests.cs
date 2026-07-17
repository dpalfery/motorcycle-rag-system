using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class BikeModelRepositoryTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new BikeModelRepository(null!, TestHelpers.CreateNullLogger<BikeModelRepository>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new BikeModelRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── GetByIdAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_ShouldReturnMappedBikeModel_WhenFound()
    {
        var bikeModel = CreateBikeModel();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreateBikeModelRow(bikeModel)),
            command => command.Parameters["Id"].Should().Be(bikeModel.Id));
        var sut = CreateSut(connection);

        var result = await sut.GetByIdAsync(bikeModel.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(bikeModel.Id);
        result.Make.Should().Be("Honda");
        result.Model.Should().Be("CBR 1000RR");
        result.Year.Should().Be(2024);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenNoRowExists()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);
        var id = Guid.NewGuid();

        var act = async () => await sut.GetByIdAsync(id);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain($"Failed to get bike model {id}");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── FindCanonicalAsync ───────────────────────────────────────────────

    [Fact]
    public async Task FindCanonicalAsync_ShouldReturnMappedBikeModel_WhenFound()
    {
        var bikeModel = CreateBikeModel();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreateBikeModelRow(bikeModel)),
            command =>
            {
                command.Parameters["Make"].Should().Be("Honda");
                command.Parameters["Model"].Should().Be("CBR 1000RR");
                command.Parameters["Year"].Should().Be(2024);
            });
        var sut = CreateSut(connection);

        var result = await sut.FindCanonicalAsync("Honda", "CBR 1000RR", 2024);

        result.Should().NotBeNull();
        result!.Make.Should().Be("Honda");
    }

    [Fact]
    public async Task FindCanonicalAsync_ShouldReturnNull_WhenNoRowExists()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.FindCanonicalAsync("Yamaha", "R1", 2023);

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindCanonicalAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.FindCanonicalAsync("Ducati", "Panigale", 2022);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to find canonical bike model Ducati Panigale 2022");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── UpsertAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertAsync_ShouldThrowArgumentNullException_WhenBikeModelIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.UpsertAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("bikeModel");
    }

    [Fact]
    public async Task UpsertAsync_ShouldReturnGeneratedId_WhenMergeSucceeds()
    {
        var bikeModel = CreateBikeModel();
        var connection = new FakeDbConnection();
        connection.EnqueueScalar(
            bikeModel.Id,
            command =>
            {
                command.CommandText.Should().Contain("MERGE [dbo].[BikeModels]");
                command.Parameters["Id"].Should().Be(bikeModel.Id);
                command.Parameters["Make"].Should().Be("Honda");
                command.Parameters["Model"].Should().Be("CBR 1000RR");
                command.Parameters["Year"].Should().Be(2024);
                command.Parameters["Aliases"].Should().Be(bikeModel.Aliases);
                command.Parameters["CreatedAtUtc"].Should().Be(bikeModel.CreatedAtUtc.UtcDateTime);
                command.Parameters["UpdatedAtUtc"].Should().Be(bikeModel.UpdatedAtUtc.UtcDateTime);
            });
        var sut = CreateSut(connection);

        var result = await sut.UpsertAsync(bikeModel);

        result.Should().Be(bikeModel.Id);
    }

    [Fact]
    public async Task UpsertAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.UpsertAsync(CreateBikeModel());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to upsert bike model Honda CBR 1000RR 2024");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── ListAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ShouldReturnMappedBikeModels()
    {
        var bikeModel1 = CreateBikeModel(make: "Honda");
        var bikeModel2 = CreateBikeModel(make: "Yamaha");
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreateBikeModelRow(bikeModel1), CreateBikeModelRow(bikeModel2)),
            command =>
            {
                command.Parameters["Skip"].Should().Be(0);
                command.Parameters["Take"].Should().Be(20);
            });
        var sut = CreateSut(connection);

        var result = await sut.ListAsync(0, 20);

        result.Should().HaveCount(2);
        result.Select(b => b.Make).Should().Contain(["Honda", "Yamaha"]);
    }

    [Fact]
    public async Task ListAsync_ShouldReturnEmptyList_WhenNoRowsExist()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.ListAsync(0, 10);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.ListAsync(5, 15);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to list bike models (skip=5, take=15)");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static BikeModelRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new BikeModelRepository(factory.Object, TestHelpers.CreateNullLogger<BikeModelRepository>());
    }

    private static BikeModelRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new BikeModelRepository(factory.Object, TestHelpers.CreateNullLogger<BikeModelRepository>());
    }

    private static BikeModel CreateBikeModel(string make = "Honda", string model = "CBR 1000RR", int year = 2024) =>
        BikeModel.Rehydrate(
            id: Guid.NewGuid(),
            make,
            model,
            year,
            aliases: "Fireblade",
            createdAtUtc: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
            updatedAtUtc: new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
            createdByUserId: null,
            uploadRef: null);

    private static Dictionary<string, object?> CreateBikeModelRow(BikeModel bikeModel) =>
        new()
        {
            ["Id"] = bikeModel.Id,
            ["Make"] = bikeModel.Make,
            ["Model"] = bikeModel.Model,
            ["Year"] = bikeModel.Year,
            ["Aliases"] = bikeModel.Aliases,
            ["CreatedAtUtc"] = bikeModel.CreatedAtUtc,
            ["UpdatedAtUtc"] = bikeModel.UpdatedAtUtc
        };
}

