using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class UserRepositoryTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new UserRepository(null!, NullLogger<UserRepository>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new UserRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task CreateUserAsync_ShouldThrowArgumentNullException_WhenUserIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.CreateUserAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("user");
    }

    [Fact]
    public async Task CreateUserAsync_ShouldReturnCreatedUser()
    {
        var user = CreateUser();
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreateUserRow(user.Id, user.Email)),
            command =>
            {
                command.CommandText.Should().Contain("INSERT INTO [dbo].[Users]");
                command.Parameters["Id"].Should().Be(user.Id);
                command.Parameters["Email"].Should().Be(user.Email);
                command.Parameters["DisplayName"].Should().Be(user.DisplayName);
                command.Parameters["TierLabel"].Should().Be((int?)user.TierLabel!);
            });
        var sut = CreateSut(connection);

        var result = await sut.CreateUserAsync(user);

        result.Should().NotBeNull();
        result.Id.Should().Be(user.Id);
        result.Email.Should().Be(user.Email);
        result.DisplayName.Should().Be(user.DisplayName);
    }

    [Fact]
    public async Task CreateUserAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.CreateUserAsync(CreateUser());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to create user");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetUserByIdAsync_ShouldThrowArgumentException_WhenUserIdIsBlank(string? userId)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetUserByIdAsync(userId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("userId");
    }

    [Fact]
    public async Task GetUserByIdAsync_ShouldReturnUser_WhenFound()
    {
        var userId = "user-123";
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreateUserRow(userId, "rider@example.com")),
            command =>
            {
                command.Parameters["UserId"].Should().Be(userId);
            });
        var sut = CreateSut(connection);

        var result = await sut.GetUserByIdAsync(userId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(userId);
        result.Email.Should().Be("rider@example.com");
    }

    [Fact]
    public async Task GetUserByIdAsync_ShouldReturnNull_WhenNotFound()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.GetUserByIdAsync("user-999");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetUserByIdAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetUserByIdAsync("user-1");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to get user by ID user-1");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetUserByEmailAsync_ShouldThrowArgumentException_WhenEmailIsBlank(string? email)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetUserByEmailAsync(email!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("email");
    }

    [Fact]
    public async Task GetUserByEmailAsync_ShouldReturnUser_WhenFound()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreateUserRow("user-1", "rider@example.com")),
            command =>
            {
                command.Parameters["Email"].Should().Be("rider@example.com");
            });
        var sut = CreateSut(connection);

        var result = await sut.GetUserByEmailAsync("rider@example.com");

        result.Should().NotBeNull();
        result!.Email.Should().Be("rider@example.com");
    }

    [Fact]
    public async Task GetUserByEmailAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetUserByEmailAsync("rider@example.com");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to get user by email rider@example.com");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldThrowArgumentNullException_WhenUserIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.UpdateUserAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("user");
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldReturnTrue_WhenRowsAffected()
    {
        var user = CreateUser();
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(1);
        var sut = CreateSut(connection);

        var result = await sut.UpdateUserAsync(user);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldReturnFalse_WhenNoRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(0);
        var sut = CreateSut(connection);

        var result = await sut.UpdateUserAsync(CreateUser());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateUserAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.UpdateUserAsync(CreateUser());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to update user with ID user-1");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SetUserEnabledStatusAsync_ShouldThrowArgumentException_WhenUserIdIsBlank(string? userId)
    {
        var sut = CreateSut();

        var act = async () => await sut.SetUserEnabledStatusAsync(userId!, true);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("userId");
    }

    [Fact]
    public async Task SetUserEnabledStatusAsync_ShouldReturnTrue_WhenRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.Parameters["UserId"].Should().Be("user-1");
                command.Parameters["IsEnabled"].Should().Be(false);
            });
        var sut = CreateSut(connection);

        var result = await sut.SetUserEnabledStatusAsync("user-1", false);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SetUserEnabledStatusAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.SetUserEnabledStatusAsync("user-1", true);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to set enabled status for user user-1");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task AssignTierAsync_ShouldThrowArgumentException_WhenUserIdIsBlank(string? userId)
    {
        var sut = CreateSut();

        var act = async () => await sut.AssignTierAsync(userId!, "plan-1", TierLabel.Trial);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("userId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task AssignTierAsync_ShouldThrowArgumentException_WhenPlanIdIsBlank(string? planId)
    {
        var sut = CreateSut();

        var act = async () => await sut.AssignTierAsync("user-1", planId!, TierLabel.Trial);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("planId");
    }

    [Fact]
    public async Task AssignTierAsync_ShouldReturnTrue_WhenRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.Parameters["UserId"].Should().Be("user-1");
                command.Parameters["PlanId"].Should().Be("plan-premium");
                command.Parameters["TierLabel"].Should().Be(TierLabel.RoadRunner.ToString());
            });
        var sut = CreateSut(connection);

        var result = await sut.AssignTierAsync("user-1", "plan-premium", TierLabel.RoadRunner);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task AssignTierAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.AssignTierAsync("user-1", "plan-1", TierLabel.Trial);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to assign tier for user user-1");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task UpdateAccessStateAsync_ShouldThrowArgumentException_WhenUserIdIsBlank(string? userId)
    {
        var sut = CreateSut();

        var act = async () => await sut.UpdateAccessStateAsync(
            userId!, ManagedUserAccessState.Active, true, null, null);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("userId");
    }

    [Fact]
    public async Task UpdateAccessStateAsync_ShouldReturnTrue_WhenRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.Parameters["UserId"].Should().Be("user-1");
                command.Parameters["AccessState"].Should().Be(ManagedUserAccessState.Cancelled.ToString());
                command.Parameters["IsEnabled"].Should().Be(false);
                command.Parameters["CancelledByUserId"].Should().Be("admin-1");
                command.Parameters["CancelReason"].Should().Be("abuse");
            });
        var sut = CreateSut(connection);

        var result = await sut.UpdateAccessStateAsync(
            "user-1", ManagedUserAccessState.Cancelled, false, "admin-1", "abuse");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAccessStateAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.UpdateAccessStateAsync(
            "user-1", ManagedUserAccessState.Active, true, null, null);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to update access state for user user-1");
        exception.Which.InnerException.Should().Be(expected);
    }

    [Fact]
    public async Task GetUsersAsync_ShouldThrowArgumentException_WhenPageLessThanOne()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetUsersAsync(0, 10);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("page");
    }

    [Fact]
    public async Task GetUsersAsync_ShouldThrowArgumentException_WhenPageSizeLessThanOne()
    {
        var sut = CreateSut();

        var act = async () => await sut.GetUsersAsync(1, 0);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("pageSize");
    }

    [Fact]
    public async Task GetUsersAsync_ShouldReturnPagedResults()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(
                CreateUserRow("user-1", "a@example.com"),
                CreateUserRow("user-2", "b@example.com")),
            command =>
            {
                command.CommandText.Should().Contain("OFFSET @Offset");
                command.Parameters["Offset"].Should().Be(10);
                command.Parameters["PageSize"].Should().Be(10);
            });
        var sut = CreateSut(connection);

        var results = await sut.GetUsersAsync(2, 10);

        results.Should().HaveCount(2);
        results[0].Id.Should().Be("user-1");
        results[1].Id.Should().Be("user-2");
    }

    [Fact]
    public async Task GetUsersAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetUsersAsync(1, 10);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to get users for page 1");
        exception.Which.InnerException.Should().Be(expected);
    }

    private static UserRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new UserRepository(factory.Object, NullLogger<UserRepository>.Instance);
    }

    private static UserRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new UserRepository(factory.Object, NullLogger<UserRepository>.Instance);
    }

    private static UserDTO CreateUser(string id = "user-1", string email = "rider@example.com") => new()
    {
        Id = id,
        Email = email,
        DisplayName = "Test Rider",
        FirstName = "Test",
        LastName = "Rider",
        IsEnabled = true,
        CreatedDate = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
        LastUpdatedDate = null,
        PlanId = "plan-basic",
        TierLabel = TierLabel.Trial,
        AccessState = ManagedUserAccessState.Active,
        CancelledAtUtc = null,
        CancelledByUserId = null,
        CancelReason = null,
        AuthProvider = "Microsoft",
        ProviderUserId = "prov-user-1",
        RowVersion = "0x01"
    };

    private static Dictionary<string, object?> CreateUserRow(string id, string email) => new()
    {
        ["Id"] = id,
        ["Email"] = email,
        ["DisplayName"] = "Test Rider",
        ["FirstName"] = "Test",
        ["LastName"] = "Rider",
        ["IsEnabled"] = true,
        ["CreatedDate"] = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc),
        ["LastUpdatedDate"] = DBNull.Value,
        ["PlanId"] = "plan-basic",
        ["TierLabel"] = (int?)TierLabel.Trial,
        ["AccessState"] = (int)ManagedUserAccessState.Active,
        ["CancelledAtUtc"] = DBNull.Value,
        ["CancelledByUserId"] = DBNull.Value,
        ["CancelReason"] = DBNull.Value,
        ["AuthProvider"] = "Microsoft",
        ["ProviderUserId"] = "prov-user-1",
        ["RowVersion"] = "0x01"
    };
}
