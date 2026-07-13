using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Persistence.Sql;
using MotorcycleRAG.Persistence.Sql.Repositories;

namespace MotorcycleRAG.UnitTests.Persistence.Sql.Repositories;

public sealed class PlanRepositoryTests
{
    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConnectionFactoryIsNull()
    {
        var act = () => new PlanRepository(null!, NullLogger<PlanRepository>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var factory = new Mock<ISqlConnectionFactory>();

        var act = () => new PlanRepository(factory.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── CreatePlanAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task CreatePlanAsync_ShouldThrowArgumentNullException_WhenPlanIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.CreatePlanAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("plan");
    }

    [Fact]
    public async Task CreatePlanAsync_ShouldReturnCreatedPlan_WhenInsertSucceeds()
    {
        var plan = CreatePlan(id: "plan-1");
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreatePlanRow(plan)),
            command =>
            {
                command.CommandText.Should().Contain("INSERT INTO [dbo].[UserPlans]");
                command.Parameters["Id"].Should().Be("plan-1");
                command.Parameters["Name"].Should().Be("Gold");
                command.Parameters["DailyRequestLimit"].Should().Be(500);
                command.Parameters["IsPaid"].Should().Be(true);
            });
        var sut = CreateSut(connection);

        var result = await sut.CreatePlanAsync(plan);

        result.Id.Should().Be("plan-1");
        result.Name.Should().Be("Gold");
        result.DailyRequestLimit.Should().Be(500);
        result.IsPaid.Should().BeTrue();
    }

    [Fact]
    public async Task CreatePlanAsync_ShouldWrapFailure_WhenNoRowIsReturned()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader());
        var sut = CreateSut(connection);

        var act = async () => await sut.CreatePlanAsync(CreatePlan());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to create plan");
        exception.Which.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Plan creation failed");
    }

    [Fact]
    public async Task CreatePlanAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.CreatePlanAsync(CreatePlan());

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to create plan");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── GetPlanByIdAsync ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetPlanByIdAsync_ShouldThrowArgumentException_WhenPlanIdIsBlank(string? planId)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetPlanByIdAsync(planId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("planId");
    }

    [Fact]
    public async Task GetPlanByIdAsync_ShouldReturnMappedPlan_WhenFound()
    {
        var plan = CreatePlan(id: "plan-2", name: "Trial");
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreatePlanRow(plan)),
            command => command.Parameters["PlanId"].Should().Be("plan-2"));
        var sut = CreateSut(connection);

        var result = await sut.GetPlanByIdAsync("plan-2");

        result.Should().NotBeNull();
        result!.Id.Should().Be("plan-2");
        result.Name.Should().Be("Trial");
    }

    [Fact]
    public async Task GetPlanByIdAsync_ShouldReturnNull_WhenNoRowExists()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.GetPlanByIdAsync("missing-plan");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPlanByIdAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetPlanByIdAsync("plan-3");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to get plan by ID plan-3");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── GetPlanByNameAsync ───────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task GetPlanByNameAsync_ShouldThrowArgumentException_WhenPlanNameIsBlank(string? planName)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetPlanByNameAsync(planName!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("planName");
    }

    [Fact]
    public async Task GetPlanByNameAsync_ShouldReturnMappedPlan_WhenFound()
    {
        var plan = CreatePlan(id: "plan-4", name: "Road Runner");
        var connection = new FakeDbConnection();
        connection.EnqueueReader(
            RepositoryTestReader.CreateReader(CreatePlanRow(plan)),
            command => command.Parameters["PlanName"].Should().Be("Road Runner"));
        var sut = CreateSut(connection);

        var result = await sut.GetPlanByNameAsync("Road Runner");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Road Runner");
    }

    [Fact]
    public async Task GetPlanByNameAsync_ShouldReturnNull_WhenNoRowExists()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.GetPlanByNameAsync("missing-plan");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPlanByNameAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetPlanByNameAsync("Gold");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to get plan by name Gold");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── GetAllPlansAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetAllPlansAsync_ShouldReturnAllMappedPlans()
    {
        var planA = CreatePlan(id: "plan-a", name: "Trial");
        var planB = CreatePlan(id: "plan-b", name: "Gold");
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader(CreatePlanRow(planA), CreatePlanRow(planB)));
        var sut = CreateSut(connection);

        var result = await sut.GetAllPlansAsync();

        result.Should().HaveCount(2);
        result.Select(p => p.Id).Should().Contain(["plan-a", "plan-b"]);
    }

    [Fact]
    public async Task GetAllPlansAsync_ShouldReturnEmptyArray_WhenNoPlansExist()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueReader(RepositoryTestReader.CreateReader());
        var sut = CreateSut(connection);

        var result = await sut.GetAllPlansAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllPlansAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.GetAllPlansAsync();

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Be("Failed to get all plans");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── UpdatePlanAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePlanAsync_ShouldThrowArgumentNullException_WhenPlanIsNull()
    {
        var sut = CreateSut();

        var act = async () => await sut.UpdatePlanAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("plan");
    }

    [Fact]
    public async Task UpdatePlanAsync_ShouldReturnTrue_WhenRowsAffected()
    {
        var plan = CreatePlan(id: "plan-5", name: "Updated");
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.CommandText.Should().Contain("UPDATE [dbo].[UserPlans]");
                command.Parameters["Id"].Should().Be("plan-5");
                command.Parameters["Name"].Should().Be("Updated");
            });
        var sut = CreateSut(connection);

        var result = await sut.UpdatePlanAsync(plan);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdatePlanAsync_ShouldReturnFalse_WhenNoRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(0);
        var sut = CreateSut(connection);

        var result = await sut.UpdatePlanAsync(CreatePlan(id: "missing-plan"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdatePlanAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.UpdatePlanAsync(CreatePlan(id: "plan-6"));

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to update plan with ID plan-6");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── DeletePlanAsync ──────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task DeletePlanAsync_ShouldThrowArgumentException_WhenPlanIdIsBlank(string? planId)
    {
        var sut = CreateSut();

        var act = async () => await sut.DeletePlanAsync(planId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("planId");
    }

    [Fact]
    public async Task DeletePlanAsync_ShouldReturnTrue_WhenRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(
            1,
            command =>
            {
                command.CommandText.Should().Contain("DELETE FROM [dbo].[UserPlans]");
                command.Parameters["PlanId"].Should().Be("plan-7");
            });
        var sut = CreateSut(connection);

        var result = await sut.DeletePlanAsync("plan-7");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeletePlanAsync_ShouldReturnFalse_WhenNoRowsAffected()
    {
        var connection = new FakeDbConnection();
        connection.EnqueueNonQuery(0);
        var sut = CreateSut(connection);

        var result = await sut.DeletePlanAsync("missing-plan");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeletePlanAsync_ShouldWrapConnectionFailures()
    {
        var expected = new InvalidOperationException("boom");
        var sut = CreateThrowingSut(expected);

        var act = async () => await sut.DeletePlanAsync("plan-8");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Failed to delete plan with ID plan-8");
        exception.Which.InnerException.Should().Be(expected);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static PlanRepository CreateSut(FakeDbConnection? connection = null)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory
            .Setup(x => x.CreateOpenConnectionAsync())
            .ReturnsAsync(connection ?? new FakeDbConnection());
        return new PlanRepository(factory.Object, NullLogger<PlanRepository>.Instance);
    }

    private static PlanRepository CreateThrowingSut(Exception exception)
    {
        var factory = new Mock<ISqlConnectionFactory>();
        factory.Setup(x => x.CreateOpenConnectionAsync()).ThrowsAsync(exception);
        return new PlanRepository(factory.Object, NullLogger<PlanRepository>.Instance);
    }

    private static UserPlan CreatePlan(
        string id = "plan-1",
        string name = "Gold",
        string description = "Gold tier plan",
        int dailyRequestLimit = 500,
        bool isPaid = true) =>
        new()
        {
            Id = id,
            Name = name,
            Description = description,
            DailyRequestLimit = dailyRequestLimit,
            IsPaid = isPaid,
            CreatedDate = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc)
        };

    private static Dictionary<string, object?> CreatePlanRow(UserPlan plan) =>
        new()
        {
            ["Id"] = plan.Id,
            ["Name"] = plan.Name,
            ["Description"] = plan.Description,
            ["DailyRequestLimit"] = plan.DailyRequestLimit,
            ["IsPaid"] = plan.IsPaid,
            ["CreatedDate"] = plan.CreatedDate
        };
}
