using System.Data;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class BikeModelCategoryRepositoryTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new BikeModelCategoryRepository(null!, TestHelpers.CreateNullLogger<BikeModelCategoryRepository>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>().Object;

        var act = () => new BikeModelCategoryRepository(factory, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetCategoryAsync_ShouldThrowArgumentException_WhenMakeIsBlank(string? make)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetCategoryAsync(make!, "CBR1000RR");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("make");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetCategoryAsync_ShouldThrowArgumentException_WhenModelIsBlank(string? model)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetCategoryAsync("Honda", model!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("model");
    }

    [Fact]
    public async Task GetCategoryAsync_ShouldReturnCategory_WhenRowExists()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(new Dictionary<string, object?> { ["Category"] = "Sport" }),
            command =>
            {
                command.CommandText.Should().Contain("[BikeModelCategory]");
                command.Parameters["Make"].Should().Be("Honda");
                command.Parameters["Model"].Should().Be("CBR1000RR");
            });
        var sut = CreateSut(connection);

        var result = await sut.GetCategoryAsync("Honda", "CBR1000RR");

        result.Should().Be("Sport");
    }

    [Fact]
    public async Task GetCategoryAsync_ShouldReturnNull_WhenNoRowExists()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.GetCategoryAsync("Honda", "Unknown Model");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCategoryAsync_ShouldWrapDatabaseFailures()
    {
        var connection = new FakeDbConnection();
        var expected = new DataException("read failed");
        connection.EnqueueReaderException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.GetCategoryAsync("Honda", "CBR1000RR");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Honda");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task UpsertAsync_ShouldThrowArgumentException_WhenMakeIsBlank(string? make)
    {
        var sut = CreateSut();

        var act = async () => await sut.UpsertAsync(make!, "CBR1000RR", "Sport");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("make");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task UpsertAsync_ShouldThrowArgumentException_WhenModelIsBlank(string? model)
    {
        var sut = CreateSut();

        var act = async () => await sut.UpsertAsync("Honda", model!, "Sport");

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("model");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task UpsertAsync_ShouldThrowArgumentException_WhenCategoryIsBlank(string? category)
    {
        var sut = CreateSut();

        var act = async () => await sut.UpsertAsync("Honda", "CBR1000RR", category!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("category");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task UpsertAsync_ShouldThrowArgumentException_WhenSourceIsBlank(string? source)
    {
        var sut = CreateSut();

        var act = async () => await sut.UpsertAsync("Honda", "CBR1000RR", "Sport", source!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("source");
    }

    [Fact]
    public async Task UpsertAsync_ShouldThrowArgumentException_WhenCategoryTokenIsInvalid_AndNotTouchConnection()
    {
        var connection = new FakeDbConnection();
        var sut = CreateSut(connection);

        var act = async () => await sut.UpsertAsync("Honda", "CBR1000RR", "Naked");

        var exception = await act.Should().ThrowAsync<ArgumentException>();
        exception.Which.ParamName.Should().Be("category");
        connection.ExecutedCommands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("dirt", "Dirt")]
    [InlineData("TOURING", "Touring")]
    [InlineData("Sport", "Sport")]
    [InlineData(" cruiser ", "Cruiser")]
    public async Task UpsertAsync_ShouldMapCategoryToStorageForm_ForAnyValidCasing(string input, string expectedStorageValue)
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.CommandText.Should().Contain("MERGE [dbo].[BikeModelCategory]");
                command.Parameters["Category"].Should().Be(expectedStorageValue);
                command.Parameters["Make"].Should().Be("Honda");
                command.Parameters["Model"].Should().Be("CBR1000RR");
            });
        var sut = CreateSut(connection);

        await sut.UpsertAsync("Honda", "CBR1000RR", input);

        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Fact]
    public async Task UpsertAsync_ShouldDefaultSourceToClassifier_WhenNotProvided()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command => command.Parameters["Source"].Should().Be("Classifier"));
        var sut = CreateSut(connection);

        await sut.UpsertAsync("Honda", "CBR1000RR", "Sport");

        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Fact]
    public async Task UpsertAsync_ShouldUseProvidedSource_WhenSpecified()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command => command.Parameters["Source"].Should().Be("Manual"));
        var sut = CreateSut(connection);

        await sut.UpsertAsync("Honda", "CBR1000RR", "Sport", "Manual");

        connection.ExecutedCommands.Should().ContainSingle();
    }

    [Fact]
    public async Task UpsertAsync_ShouldWrapDatabaseFailures()
    {
        var connection = new FakeDbConnection();
        var expected = new DataException("write failed");
        connection.EnqueueNonQueryException(expected);
        var sut = CreateSut(connection);

        var act = async () => await sut.UpsertAsync("Honda", "CBR1000RR", "Sport");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().Be(expected);
    }

    private static BikeModelCategoryRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new BikeModelCategoryRepository(factory.Object, TestHelpers.CreateNullLogger<BikeModelCategoryRepository>());
    }
}

