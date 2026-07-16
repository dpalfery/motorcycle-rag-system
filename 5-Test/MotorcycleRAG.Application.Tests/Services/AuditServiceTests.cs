using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Services;

public sealed class AuditServiceTests
{
    private readonly Mock<IAuditRepository> _auditRepository = new();
    private readonly Mock<ICorrelationService> _correlationService = new();

    public AuditServiceTests()
    {
        _correlationService.Setup(x => x.GetOrGenerateCorrelationId()).Returns("corr-1");
        _auditRepository
            .Setup(x => x.CreateAuditLogAsync(It.IsAny<AuditLog>()))
            .ReturnsAsync((AuditLog log) =>
            {
                log.Id = 99;
                return log;
            });
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenDependenciesAreNull()
    {
        var actRepo = () => new AuditService(null!, _correlationService.Object, NullLogger<AuditService>.Instance);
        var actCorrelation = () => new AuditService(_auditRepository.Object, null!, NullLogger<AuditService>.Instance);
        var actLogger = () => new AuditService(_auditRepository.Object, _correlationService.Object, null!);

        actRepo.Should().Throw<ArgumentNullException>().WithParameterName("auditRepository");
        actCorrelation.Should().Throw<ArgumentNullException>().WithParameterName("correlationService");
        actLogger.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task LogAuthenticationAsync_ShouldThrowArgumentException_WhenUserIdIsBlank(string? userId)
    {
        var sut = CreateSut();

        var act = async () => await sut.LogAuthenticationAsync(userId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(userId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("rider@example.com")]
    public async Task LogAuthenticationAsync_ShouldPersistSuccessLog_AndMaskEmailVariants(string? email)
    {
        var sut = CreateSut();

        var result = await sut.LogAuthenticationAsync("user-1", email, "127.0.0.1", "agent");

        result.Id.Should().Be(99);
        result.Action.Should().Be("authentication_success");
        result.Status.Should().Be("Success");
        result.UserId.Should().Be("user-1");
        result.EntityId.Should().Be("user-1");
        result.UserEmail.Should().Be(email);
        result.IpAddress.Should().Be("127.0.0.1");
        result.UserAgent.Should().Be("agent");
        result.Metadata.Should().Contain("corr-1");
    }

    [Fact]
    public async Task LogLogoutAsync_ShouldPersistLogoutLog()
    {
        var sut = CreateSut();

        var result = await sut.LogLogoutAsync("user-1", "10.0.0.1");

        result.Action.Should().Be("logout");
        result.Status.Should().Be("Success");
        result.IpAddress.Should().Be("10.0.0.1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task LogLogoutAsync_ShouldThrowArgumentException_WhenUserIdIsBlank(string? userId)
    {
        var sut = CreateSut();

        var act = async () => await sut.LogLogoutAsync(userId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(userId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task LogFailedAuthenticationAsync_ShouldThrowArgumentException_WhenReasonIsBlank(string? reason)
    {
        var sut = CreateSut();

        var act = async () => await sut.LogFailedAuthenticationAsync("rider@example.com", reason!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(reason));
    }

    [Fact]
    public async Task LogFailedAuthenticationAsync_ShouldPersistFailure_WithUnknownEntity_WhenEmailMissing()
    {
        var sut = CreateSut();

        var result = await sut.LogFailedAuthenticationAsync(null, "bad password", "1.1.1.1", "ua");

        result.Action.Should().Be("authentication_failure");
        result.Status.Should().Be("Failure");
        result.EntityId.Should().Be("unknown");
        result.ErrorMessage.Should().Be("bad password");
        result.IpAddress.Should().Be("1.1.1.1");
        result.UserAgent.Should().Be("ua");
    }

    [Fact]
    public async Task LogConfigurationChangeAsync_ShouldPersistConfigChange()
    {
        var sut = CreateSut();

        var result = await sut.LogConfigurationChangeAsync(
            "admin-1",
            "McpTool",
            "tool-1",
            "update",
            beforeValue: "off",
            afterValue: "on",
            changeReason: "enable tool");

        result.Action.Should().Be("config_change_update");
        result.EntityType.Should().Be("McpTool");
        result.EntityId.Should().Be("tool-1");
        result.OldValue.Should().Be("off");
        result.NewValue.Should().Be("on");
        result.Metadata.Should().Contain("enable tool");
    }

    [Theory]
    [InlineData(null, "McpTool", "tool-1", "update", "userId")]
    [InlineData("admin", null, "tool-1", "update", "entityType")]
    [InlineData("admin", "McpTool", null, "update", "entityId")]
    [InlineData("admin", "McpTool", "tool-1", null, "action")]
    public async Task LogConfigurationChangeAsync_ShouldValidateRequiredArguments(
        string? userId,
        string? entityType,
        string? entityId,
        string? action,
        string expectedParameter)
    {
        var sut = CreateSut();

        var act = async () => await sut.LogConfigurationChangeAsync(
            userId!,
            entityType!,
            entityId!,
            action!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(expectedParameter);
    }

    [Fact]
    public async Task LogDataAccessAsync_ShouldPersistSuccessAndFailureStatuses()
    {
        var sut = CreateSut();

        var success = await sut.LogDataAccessAsync("user-1", "Document", "doc-1", "read");
        var failure = await sut.LogDataAccessAsync(
            "user-1",
            "Document",
            "doc-1",
            "delete",
            status: "Failure",
            errorMessage: "denied",
            metadata: "extra");

        success.Action.Should().Be("data_access_read");
        success.Status.Should().Be("Success");
        failure.Action.Should().Be("data_access_delete");
        failure.Status.Should().Be("Failure");
        failure.ErrorMessage.Should().Be("denied");
        failure.Metadata.Should().Contain("extra");
    }

    [Fact]
    public async Task LogAdminActionAsync_ShouldPersistAdminAction()
    {
        var sut = CreateSut();

        var result = await sut.LogAdminActionAsync(
            "admin-1",
            "user-2",
            "assign_plan",
            changeDetails: "RoadRunner",
            reason: "upgrade");

        result.Action.Should().Be("admin_assign_plan");
        result.EntityId.Should().Be("user-2");
        result.UserId.Should().Be("admin-1");
        result.Metadata.Should().Contain("upgrade");
    }

    [Theory]
    [InlineData(null, "user-2", "assign_plan", "adminUserId")]
    [InlineData("admin", null, "assign_plan", "targetUserId")]
    [InlineData("admin", "user-2", null, "action")]
    public async Task LogAdminActionAsync_ShouldValidateRequiredArguments(
        string? adminUserId,
        string? targetUserId,
        string? action,
        string expectedParameter)
    {
        var sut = CreateSut();

        var act = async () => await sut.LogAdminActionAsync(adminUserId!, targetUserId!, action!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(expectedParameter);
    }

    [Fact]
    public async Task LogRateLimitViolationAsync_ShouldPersistAnonymousAndNamedUsers()
    {
        var sut = CreateSut();

        var anonymous = await sut.LogRateLimitViolationAsync(null, "/api/query", "2.2.2.2", 10, 30);
        var named = await sut.LogRateLimitViolationAsync("user-1", "/api/query");

        anonymous.UserId.Should().Be("anonymous");
        anonymous.Action.Should().Be("rate_limit_exceeded");
        anonymous.EntityId.Should().Be("/api/query");
        anonymous.ErrorMessage.Should().Contain("10 requests per 30 seconds");
        named.UserId.Should().Be("user-1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task LogRateLimitViolationAsync_ShouldThrowArgumentException_WhenEndpointIsBlank(string? endpoint)
    {
        var sut = CreateSut();

        var act = async () => await sut.LogRateLimitViolationAsync("user-1", endpoint!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(endpoint));
    }

    [Theory]
    [InlineData("Critical")]
    [InlineData("High")]
    [InlineData("Medium")]
    [InlineData("Low")]
    public async Task LogSecurityEventAsync_ShouldPersistAcrossSeverities(string severity)
    {
        var sut = CreateSut();

        var result = await sut.LogSecurityEventAsync(
            null,
            "authz_denied",
            "missing role",
            ipAddress: "3.3.3.3",
            severity: severity);

        result.UserId.Should().Be("system");
        result.Action.Should().Be("security_event_authz_denied");
        result.ErrorMessage.Should().Be("missing role");
        result.Metadata.Should().Contain(severity);
    }

    [Theory]
    [InlineData(null, "desc", "eventType")]
    [InlineData("type", null, "description")]
    public async Task LogSecurityEventAsync_ShouldValidateRequiredArguments(
        string? eventType,
        string? description,
        string expectedParameter)
    {
        var sut = CreateSut();

        var act = async () => await sut.LogSecurityEventAsync("user-1", eventType!, description!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(expectedParameter);
    }

    [Fact]
    public async Task GetAuditLogsAsync_ShouldReturnRepositoryResults()
    {
        var expected = new[] { new AuditLog { Id = 1, Action = "logout" } };
        _auditRepository
            .Setup(x => x.GetAuditLogsByEntityAsync("User", "user-1"))
            .ReturnsAsync(expected);
        var sut = CreateSut();

        var result = await sut.GetAuditLogsAsync("User", "user-1");

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task GetAuditLogsAsync_ShouldWrapRepositoryFailures()
    {
        _auditRepository
            .Setup(x => x.GetAuditLogsByEntityAsync("User", "user-1"))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateSut();

        var act = async () => await sut.GetAuditLogsAsync("User", "user-1");

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("Error retrieving audit logs for entity User:user-1");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10001)]
    public async Task GetRecentAuditLogsAsync_ShouldThrowArgumentException_WhenLimitOutOfRange(int limit)
    {
        var sut = CreateSut();

        var act = async () => await sut.GetRecentAuditLogsAsync(limit);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(limit));
    }

    [Fact]
    public async Task GetRecentAuditLogsAsync_ShouldReturnRepositoryResults()
    {
        var expected = new[] { new AuditLog { Id = 2 } };
        _auditRepository.Setup(x => x.GetRecentAuditLogsAsync(25)).ReturnsAsync(expected);
        var sut = CreateSut();

        var result = await sut.GetRecentAuditLogsAsync(25);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task GetRecentAuditLogsAsync_ShouldWrapRepositoryFailures()
    {
        _auditRepository
            .Setup(x => x.GetRecentAuditLogsAsync(10))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateSut();

        var act = async () => await sut.GetRecentAuditLogsAsync(10);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Error retrieving recent audit logs*");
    }

    private AuditService CreateSut() =>
        new(_auditRepository.Object, _correlationService.Object, NullLogger<AuditService>.Instance);
}
