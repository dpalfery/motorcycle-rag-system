using System.Collections.ObjectModel;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MotorcycleRAG.API.Controllers;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Presentation.API.Controllers;

public sealed class MotorcycleControllerTests
{
    [Fact]
    public void Constructor_WithNullDependencies_ThrowsArgumentNullException()
    {
        var rag = new Mock<IMotorcycleRagService>();
        var user = new Mock<ICurrentUserService>();
        var plans = new Mock<IPlanPolicyService>();
        var usage = new Mock<IUsageTrackingService>();

        ((Action)(() => new MotorcycleController(null!, user.Object, plans.Object, usage.Object, NullLogger<MotorcycleController>.Instance))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new MotorcycleController(rag.Object, null!, plans.Object, usage.Object, NullLogger<MotorcycleController>.Instance))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new MotorcycleController(rag.Object, user.Object, null!, usage.Object, NullLogger<MotorcycleController>.Instance))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new MotorcycleController(rag.Object, user.Object, plans.Object, null!, NullLogger<MotorcycleController>.Instance))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new MotorcycleController(rag.Object, user.Object, plans.Object, usage.Object, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task QueryAsync_WithNullRequest_ThrowsArgumentNullException()
    {
        var controller = CreateController(out _, out _, out _, out _);

        var act = () => controller.QueryAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task QueryAsync_WhenUnauthenticated_ReturnsUnauthorized()
    {
        var controller = CreateController(out _, out var user, out _, out _);
        user.SetupGet(service => service.IsAuthenticated).Returns(false);

        var result = await controller.QueryAsync(ValidRequest());

        AssertStatus(result, StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task QueryAsync_WhenManagedUserIsUnavailable_ReturnsForbidden()
    {
        var controller = CreateAuthenticatedController(out _, out var user, out _, out _);
        user.Setup(service => service.GetManagedUserIdAsync()).ReturnsAsync((string?)null);

        var result = await controller.QueryAsync(ValidRequest());

        AssertStatus(result, StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task QueryAsync_WhenDailyLimitIsExceeded_ReturnsTooManyRequests()
    {
        var controller = CreateAuthenticatedController(out _, out _, out var plans, out _);
        plans.Setup(service => service.HasExceededDailyLimitAsync("user-1", null)).ReturnsAsync(true);
        plans.Setup(service => service.GetRemainingDailyRequestsAsync("user-1", null)).ReturnsAsync(0);

        var result = await controller.QueryAsync(ValidRequest());

        AssertStatus(result, StatusCodes.Status429TooManyRequests);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task QueryAsync_WithInvalidBusinessRequest_ReturnsBadRequest(MotorcycleQueryRequest request)
    {
        var controller = CreateAuthenticatedController(out var rag, out _, out var plans, out _);
        plans.Setup(service => service.HasExceededDailyLimitAsync("user-1", null)).ReturnsAsync(false);

        var result = await controller.QueryAsync(request);

        AssertStatus(result, StatusCodes.Status400BadRequest);
        rag.Verify(service => service.QueryAsync(It.IsAny<MotorcycleQueryRequest>()), Times.Never);
    }

    [Fact]
    public async Task QueryAsync_WhenRagSucceeds_ReturnsResponseAndRecordsSuccess()
    {
        var controller = CreateAuthenticatedController(out var rag, out _, out var plans, out var usage);
        var response = new MotorcycleQueryResponse { QueryId = "query-1", Response = "Use the service manual." };
        plans.Setup(service => service.HasExceededDailyLimitAsync("user-1", null)).ReturnsAsync(false);
        rag.Setup(service => service.QueryAsync(It.IsAny<MotorcycleQueryRequest>())).ReturnsAsync(response);
        usage.Setup(service => service.RecordSuccessAsync(
                "user-1", "/api/motorcycles/query", "POST", "query-1", It.IsAny<long>(), System.Net.IPAddress.Loopback.ToString(), "unit-test"))
            .ReturnsAsync(new Usage());
        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(System.Net.IPAddress.Loopback) };

        var result = await controller.QueryAsync(ValidRequest());

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(response);
        usage.VerifyAll();
    }

    [Theory]
    [InlineData(true, StatusCodes.Status400BadRequest)]
    [InlineData(false, StatusCodes.Status500InternalServerError)]
    public async Task QueryAsync_WhenRagFails_RecordsFailureAndReturnsSafeStatus(bool isArgumentError, int expectedStatus)
    {
        var controller = CreateAuthenticatedController(out var rag, out _, out var plans, out var usage);
        plans.Setup(service => service.HasExceededDailyLimitAsync("user-1", null)).ReturnsAsync(false);
        rag.Setup(service => service.QueryAsync(It.IsAny<MotorcycleQueryRequest>()))
            .ThrowsAsync(isArgumentError ? new ArgumentException("bad input") : new InvalidOperationException("internal detail"));
        usage.Setup(service => service.RecordFailureAsync(
                "user-1", "/api/motorcycles/query", "POST", expectedStatus, null, It.IsAny<long>(), System.Net.IPAddress.Loopback.ToString(), It.IsAny<string?>()))
            .ReturnsAsync(new Usage());

        controller.ControllerContext = new ControllerContext { HttpContext = CreateHttpContext(System.Net.IPAddress.Loopback) };

        var result = await controller.QueryAsync(ValidRequest());

        AssertStatus(result, expectedStatus);
        usage.VerifyAll();
    }

    [Fact]
    public async Task QueryAsync_WhenSuccessUsageRecordingFails_RecordsServerFailure()
    {
        var controller = CreateAuthenticatedController(out var rag, out _, out var plans, out var usage);
        var response = new MotorcycleQueryResponse { QueryId = "query-1", Response = "Use the service manual." };
        plans.Setup(service => service.HasExceededDailyLimitAsync("user-1", null)).ReturnsAsync(false);
        rag.Setup(service => service.QueryAsync(It.IsAny<MotorcycleQueryRequest>())).ReturnsAsync(response);
        usage.Setup(service => service.RecordSuccessAsync(
                "user-1", "/api/motorcycles/query", "POST", "query-1", It.IsAny<long>(), null, "unit-test"))
            .ThrowsAsync(new InvalidOperationException("usage persistence failed"));
        usage.Setup(service => service.RecordFailureAsync(
                "user-1", "/api/motorcycles/query", "POST", StatusCodes.Status500InternalServerError, null, It.IsAny<long>(), null, "unit-test"))
            .ReturnsAsync(new Usage());

        var result = await controller.QueryAsync(ValidRequest());

        AssertStatus(result, StatusCodes.Status500InternalServerError);
        usage.VerifyAll();
    }

    [Fact]
    public async Task HealthAsync_ReturnsRagHealthResult()
    {
        var controller = CreateController(out var rag, out _, out _, out _);
        var health = new HealthCheckResult { IsHealthy = true, Status = "healthy" };
        rag.Setup(service => service.GetHealthAsync()).ReturnsAsync(health);

        var result = await controller.HealthAsync();

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeSameAs(health);
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return [new MotorcycleQueryRequest { Query = string.Empty }];
        yield return [new MotorcycleQueryRequest { Query = "ab" }];
        yield return [new MotorcycleQueryRequest { Query = new string('x', 1001) }];
        yield return [new MotorcycleQueryRequest
        {
            Query = "valid query",
            Preferences = new SearchPreferences { MaxResults = 0, MinRelevanceScore = -0.1f },
            UserId = new string('u', 101),
        }];
        yield return [new MotorcycleQueryRequest
        {
            Query = "valid query",
            Preferences = new SearchPreferences
            {
                MaxResults = 101,
                MinRelevanceScore = 1.1f,
                PreferredSources = new Collection<string>(Enumerable.Repeat("source", 11).ToList()),
            },
        }];
        yield return [new MotorcycleQueryRequest
        {
            Query = "valid query",
            Preferences = new SearchPreferences
            {
                PreferredSources = new Collection<string>(["", new string('x', 101), "not valid!" ]),
            },
        }];
    }

    private static MotorcycleController CreateAuthenticatedController(
        out Mock<IMotorcycleRagService> rag,
        out Mock<ICurrentUserService> user,
        out Mock<IPlanPolicyService> plans,
        out Mock<IUsageTrackingService> usage)
    {
        var controller = CreateController(out rag, out user, out plans, out usage);
        user.SetupGet(service => service.IsAuthenticated).Returns(true);
        user.Setup(service => service.GetManagedUserIdAsync()).ReturnsAsync("user-1");
        return controller;
    }

    private static MotorcycleController CreateController(
        out Mock<IMotorcycleRagService> rag,
        out Mock<ICurrentUserService> user,
        out Mock<IPlanPolicyService> plans,
        out Mock<IUsageTrackingService> usage)
    {
        rag = new Mock<IMotorcycleRagService>(MockBehavior.Loose);
        user = new Mock<ICurrentUserService>(MockBehavior.Loose);
        plans = new Mock<IPlanPolicyService>(MockBehavior.Loose);
        usage = new Mock<IUsageTrackingService>(MockBehavior.Loose);
        return new MotorcycleController(rag.Object, user.Object, plans.Object, usage.Object, NullLogger<MotorcycleController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = CreateHttpContext() },
        };
    }

    private static MotorcycleQueryRequest ValidRequest() => new() { Query = "How do I adjust the chain?" };

    private static DefaultHttpContext CreateHttpContext(System.Net.IPAddress? remoteIpAddress = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIpAddress;
        context.Request.Headers.UserAgent = "unit-test";
        return context;
    }

    private static void AssertStatus(IActionResult result, int expectedStatus) =>
        result.Should().BeAssignableTo<ObjectResult>().Which.StatusCode.Should().Be(expectedStatus);
}
