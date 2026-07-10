using System.Net;
using System.Net.Http.Json;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Contrib.HttpClient;
using Moq.Protected;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Persistence.Azure;

namespace MotorcycleRAG.UnitTests.Persistence.Azure;

public sealed class ExternalIdentityProvisioningServiceTests
{
    private const string GraphBaseUrl = "https://graph.test/v1.0";
    private const string ApiServicePrincipalObjectId = "11111111-1111-1111-1111-111111111111";
    private const string TrialAppRoleId = "22222222-2222-2222-2222-222222222222";
    private const string RoadRunnerAppRoleId = "33333333-3333-3333-3333-333333333333";
    private const string AdminAppRoleId = "44444444-4444-4444-4444-444444444444";
    private const string InviteRedirectUrl = "https://app.example.com/signin";

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenHttpClientIsNull()
    {
        var options = CreateOptions();

        var act = () => new ExternalIdentityProvisioningService(
            null!,
            options,
            NullLogger<ExternalIdentityProvisioningService>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("httpClient");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsIsNull()
    {
        using var httpClient = new HttpClient();

        var act = () => new ExternalIdentityProvisioningService(
            httpClient,
            null!,
            NullLogger<ExternalIdentityProvisioningService>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenOptionsValueIsNull()
    {
        using var httpClient = new HttpClient();
        var options = new Mock<IOptions<ExternalIdentityProvisioningOptions>>();
        options.SetupGet(x => x.Value).Returns((ExternalIdentityProvisioningOptions)null!);

        var act = () => new ExternalIdentityProvisioningService(
            httpClient,
            options.Object,
            NullLogger<ExternalIdentityProvisioningService>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        using var httpClient = new HttpClient();
        var options = CreateOptions();

        var act = () => new ExternalIdentityProvisioningService(httpClient, options, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ProvisionApprovedUserAsync_ShouldThrowArgumentException_WhenEmailIsBlank(string? email)
    {
        using var httpClient = new HttpClient();
        var sut = CreateSut(httpClient);

        var act = async () => await sut.ProvisionApprovedUserAsync(email!, "Rider Example", TierLabel.Trial, IdentityProvider.Microsoft);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(email));
    }

    [Fact]
    public async Task ProvisionApprovedUserAsync_ShouldThrowInvalidOperationException_WhenServicePrincipalConfigurationIsMissing()
    {
        using var httpClient = new HttpClient();
        var sut = CreateSut(
            httpClient,
            configure: options =>
            {
                options.ApiServicePrincipalObjectId = string.Empty;
                options.ApiApplicationClientId = string.Empty;
            });

        var act = async () => await sut.ProvisionApprovedUserAsync(
            "rider@example.com",
            "Rider Example",
            TierLabel.Trial,
            IdentityProvider.Microsoft);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ApiServicePrincipalObjectId or ExternalIdentityProvisioning:ApiApplicationClientId*");
    }

    [Fact]
    public async Task ProvisionApprovedUserAsync_ShouldReconcileExistingUserWithoutSendingInvitation()
    {
        var externalDirectoryObjectId = "55555555-5555-5555-5555-555555555555";
        var handler = CreateOrderedHandler(
            Plan(HttpMethod.Get, UsersLookupUrl("rider@example.com"), () => JsonResponse(HttpStatusCode.OK, """
                {
                  "value": [
                    {
                      "id": "55555555-5555-5555-5555-555555555555",
                      "userType": "Guest",
                      "mail": "rider@example.com",
                      "userPrincipalName": "rider_example.com#EXT#@tenant.onmicrosoft.com",
                      "otherMails": [ "alt@example.com" ]
                    }
                  ]
                }
                """)),
            Plan(HttpMethod.Get, ServicePrincipalUrl(), () => JsonResponse(HttpStatusCode.OK, ServicePrincipalResponseJson())),
            Plan(HttpMethod.Get, UserAssignmentsUrl(externalDirectoryObjectId), () => JsonResponse(HttpStatusCode.OK, AssignmentsResponseJson(
                AssignmentJson("assignment-trial", TrialAppRoleId, ApiServicePrincipalObjectId)))));

        using var httpClient = handler.CreateClient();
        var sut = CreateSut(httpClient);

        var result = await sut.ProvisionApprovedUserAsync(
            "rider@example.com",
            "Rider Example",
            TierLabel.Trial,
            IdentityProvider.Microsoft);

        result.Should().Be(externalDirectoryObjectId);
        handler.VerifyRequest(HttpMethod.Get, UsersLookupUrl("rider@example.com"), Times.Once());
        handler.VerifyRequest(HttpMethod.Post, $"{GraphBaseUrl}/invitations", Times.Never());
        handler.VerifyRequest(HttpMethod.Post, CreateAssignmentUrl(), Times.Never());
    }

    [Fact]
    public async Task ProvisionApprovedUserAsync_ShouldInviteNewUserAndAssignRequestedTier()
    {
        var externalDirectoryObjectId = "66666666-6666-6666-6666-666666666666";
        var handler = CreateOrderedHandler(
            Plan(HttpMethod.Get, UsersLookupUrl("rider@example.com"), () => JsonResponse(HttpStatusCode.OK, """{ "value": [] }""")),
            Plan(
                HttpMethod.Post,
                $"{GraphBaseUrl}/invitations",
                () => JsonResponse(HttpStatusCode.Created, """
                    {
                      "invitedUser": {
                        "id": "66666666-6666-6666-6666-666666666666"
                      }
                    }
                    """),
                request => AssertInvitationPayload(
                    request,
                    "rider@example.com",
                    "Rider Example",
                    InviteRedirectUrl,
                    true)),
            Plan(HttpMethod.Get, ServicePrincipalUrl(), () => JsonResponse(HttpStatusCode.OK, ServicePrincipalResponseJson())),
            Plan(HttpMethod.Get, UserAssignmentsUrl(externalDirectoryObjectId), () => JsonResponse(HttpStatusCode.OK, """{ "value": [] }""")),
            Plan(
                HttpMethod.Post,
                CreateAssignmentUrl(),
                () => JsonResponse(HttpStatusCode.Created, """{ "id": "assignment-roadrunner" }"""),
                request => AssertAssignmentPayload(request, externalDirectoryObjectId, ApiServicePrincipalObjectId, RoadRunnerAppRoleId)));

        using var httpClient = handler.CreateClient();
        var sut = CreateSut(httpClient);

        var result = await sut.ProvisionApprovedUserAsync(
            "rider@example.com",
            "Rider Example",
            TierLabel.RoadRunner,
            IdentityProvider.Microsoft);

        result.Should().Be(externalDirectoryObjectId);
        handler.VerifyRequest(HttpMethod.Post, $"{GraphBaseUrl}/invitations", Times.Once());
        handler.VerifyRequest(HttpMethod.Post, CreateAssignmentUrl(), Times.Once());
    }

    [Fact]
    public async Task ProvisionApprovedUserAsync_ShouldThrowInvalidOperationException_WhenInvitationResponseHasNoUserIdAndFallbackLookupFails()
    {
        var handler = CreateOrderedHandler(
            Plan(HttpMethod.Get, UsersLookupUrl("rider@example.com"), () => JsonResponse(HttpStatusCode.OK, """{ "value": [] }""")),
            Plan(HttpMethod.Post, $"{GraphBaseUrl}/invitations", () => JsonResponse(HttpStatusCode.Created, """{ "status": "Accepted" }""")),
            Plan(HttpMethod.Get, UsersLookupUrl("rider@example.com"), () => JsonResponse(HttpStatusCode.OK, """{ "value": [] }""")));

        using var httpClient = handler.CreateClient();
        var sut = CreateSut(httpClient);

        var act = async () => await sut.ProvisionApprovedUserAsync(
            "rider@example.com",
            "Rider Example",
            TierLabel.Trial,
            IdentityProvider.Microsoft);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no invited user object ID could be resolved*");
        handler.VerifyRequest(HttpMethod.Get, UsersLookupUrl("rider@example.com"), Times.Exactly(2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ReconcileTierAssignmentsAsync_ShouldThrowArgumentException_WhenExternalDirectoryObjectIdIsBlank(string? externalDirectoryObjectId)
    {
        using var httpClient = new HttpClient();
        var sut = CreateSut(httpClient);

        var act = async () => await sut.ReconcileTierAssignmentsAsync(externalDirectoryObjectId!, TierLabel.Trial);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(externalDirectoryObjectId));
    }

    [Fact]
    public async Task ReconcileTierAssignmentsAsync_ShouldDeleteWrongManagedRolesAndCreateMissingDesiredRole()
    {
        var externalDirectoryObjectId = "77777777-7777-7777-7777-777777777777";
        var handler = CreateOrderedHandler(
            Plan(HttpMethod.Get, ServicePrincipalUrl(), () => JsonResponse(HttpStatusCode.OK, ServicePrincipalResponseJson())),
            Plan(HttpMethod.Get, UserAssignmentsUrl(externalDirectoryObjectId), () => JsonResponse(HttpStatusCode.OK, AssignmentsResponseJson(
                AssignmentJson("assignment-trial", TrialAppRoleId, ApiServicePrincipalObjectId),
                AssignmentJson("assignment-admin", AdminAppRoleId, ApiServicePrincipalObjectId),
                AssignmentJson("assignment-unmanaged", "88888888-8888-8888-8888-888888888888", ApiServicePrincipalObjectId),
                AssignmentJson("assignment-other-resource", TrialAppRoleId, "99999999-9999-9999-9999-999999999999")))),
            Plan(HttpMethod.Delete, DeleteAssignmentUrl("assignment-trial"), () => EmptyResponse(HttpStatusCode.NoContent)),
            Plan(HttpMethod.Delete, DeleteAssignmentUrl("assignment-admin"), () => EmptyResponse(HttpStatusCode.NoContent)),
            Plan(
                HttpMethod.Post,
                CreateAssignmentUrl(),
                () => JsonResponse(HttpStatusCode.Created, """{ "id": "assignment-roadrunner" }"""),
                request => AssertAssignmentPayload(request, externalDirectoryObjectId, ApiServicePrincipalObjectId, RoadRunnerAppRoleId)));

        using var httpClient = handler.CreateClient();
        var sut = CreateSut(httpClient);

        await sut.ReconcileTierAssignmentsAsync(externalDirectoryObjectId, TierLabel.RoadRunner);

        handler.VerifyRequest(HttpMethod.Delete, DeleteAssignmentUrl("assignment-trial"), Times.Once());
        handler.VerifyRequest(HttpMethod.Delete, DeleteAssignmentUrl("assignment-admin"), Times.Once());
        handler.VerifyRequest(HttpMethod.Post, CreateAssignmentUrl(), Times.Once());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task RevokeAccessAsync_ShouldThrowArgumentException_WhenExternalDirectoryObjectIdIsBlank(string? externalDirectoryObjectId)
    {
        using var httpClient = new HttpClient();
        var sut = CreateSut(httpClient);

        var act = async () => await sut.RevokeAccessAsync(externalDirectoryObjectId!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName(nameof(externalDirectoryObjectId));
    }

    [Fact]
    public async Task RevokeAccessAsync_ShouldDeleteManagedAssignments()
    {
        var externalDirectoryObjectId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var handler = CreateOrderedHandler(
            Plan(HttpMethod.Get, ServicePrincipalUrl(), () => JsonResponse(HttpStatusCode.OK, ServicePrincipalResponseJson())),
            Plan(HttpMethod.Get, UserAssignmentsUrl(externalDirectoryObjectId), () => JsonResponse(HttpStatusCode.OK, AssignmentsResponseJson(
                AssignmentJson("assignment-trial", TrialAppRoleId, ApiServicePrincipalObjectId),
                AssignmentJson("assignment-admin", AdminAppRoleId, ApiServicePrincipalObjectId),
                AssignmentJson("assignment-other-resource", TrialAppRoleId, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")))),
            Plan(HttpMethod.Delete, DeleteAssignmentUrl("assignment-trial"), () => EmptyResponse(HttpStatusCode.NoContent)),
            Plan(HttpMethod.Delete, DeleteAssignmentUrl("assignment-admin"), () => EmptyResponse(HttpStatusCode.NoContent)));

        using var httpClient = handler.CreateClient();
        var sut = CreateSut(httpClient);

        await sut.RevokeAccessAsync(externalDirectoryObjectId);

        handler.VerifyRequest(HttpMethod.Delete, DeleteAssignmentUrl("assignment-trial"), Times.Once());
        handler.VerifyRequest(HttpMethod.Delete, DeleteAssignmentUrl("assignment-admin"), Times.Once());
        handler.VerifyRequest(HttpMethod.Delete, DeleteAssignmentUrl("assignment-other-resource"), Times.Never());
    }

    [Fact]
    public async Task ReconcileTierAssignmentsAsync_ShouldThrowInvalidOperationException_WhenGraphReturnsErrorResponse()
    {
        var externalDirectoryObjectId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
        var handler = CreateOrderedHandler(
            Plan(HttpMethod.Get, ServicePrincipalUrl(), () => JsonResponse(HttpStatusCode.InternalServerError, """
                {
                  "error": {
                    "code": "InternalError",
                    "message": "Graph exploded"
                  }
                }
                """)));

        using var httpClient = handler.CreateClient();
        var sut = CreateSut(httpClient);

        var act = async () => await sut.ReconcileTierAssignmentsAsync(externalDirectoryObjectId, TierLabel.Trial);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Resolve API enterprise application failed with Microsoft Graph 500 (InternalServerError).*Graph error: InternalError - Graph exploded*");
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ReconcileTierAssignmentsAsync_ShouldTreatExistingAssignmentConflictAsSuccess(HttpStatusCode statusCode)
    {
        var externalDirectoryObjectId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
        var handler = CreateOrderedHandler(
            Plan(HttpMethod.Get, ServicePrincipalUrl(), () => JsonResponse(HttpStatusCode.OK, ServicePrincipalResponseJson())),
            Plan(HttpMethod.Get, UserAssignmentsUrl(externalDirectoryObjectId), () => JsonResponse(HttpStatusCode.OK, """{ "value": [] }""")),
            Plan(HttpMethod.Post, CreateAssignmentUrl(), () => JsonResponse(statusCode, """
                {
                  "error": {
                    "code": "Request_BadRequest",
                    "message": "Assignment already exists"
                  }
                }
                """)),
            Plan(HttpMethod.Get, UserAssignmentsUrl(externalDirectoryObjectId), () => JsonResponse(HttpStatusCode.OK, AssignmentsResponseJson(
                AssignmentJson("assignment-roadrunner", RoadRunnerAppRoleId, ApiServicePrincipalObjectId)))));

        using var httpClient = handler.CreateClient();
        var sut = CreateSut(httpClient);

        await sut.ReconcileTierAssignmentsAsync(externalDirectoryObjectId, TierLabel.RoadRunner);

        handler.VerifyRequest(HttpMethod.Post, CreateAssignmentUrl(), Times.Once());
        handler.VerifyRequest(HttpMethod.Get, UserAssignmentsUrl(externalDirectoryObjectId), Times.Exactly(2));
    }

    [Fact]
    public async Task RevokeAccessAsync_ShouldTreatMissingAssignmentDeleteAsSuccess()
    {
        var externalDirectoryObjectId = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
        var handler = CreateOrderedHandler(
            Plan(HttpMethod.Get, ServicePrincipalUrl(), () => JsonResponse(HttpStatusCode.OK, ServicePrincipalResponseJson())),
            Plan(HttpMethod.Get, UserAssignmentsUrl(externalDirectoryObjectId), () => JsonResponse(HttpStatusCode.OK, AssignmentsResponseJson(
                AssignmentJson("assignment-trial", TrialAppRoleId, ApiServicePrincipalObjectId)))),
            Plan(HttpMethod.Delete, DeleteAssignmentUrl("assignment-trial"), () => EmptyResponse(HttpStatusCode.NotFound)));

        using var httpClient = handler.CreateClient();
        var sut = CreateSut(httpClient);

        await sut.RevokeAccessAsync(externalDirectoryObjectId);

        handler.VerifyRequest(HttpMethod.Delete, DeleteAssignmentUrl("assignment-trial"), Times.Once());
    }

    private static ExternalIdentityProvisioningService CreateSut(
        HttpClient httpClient,
        Action<ExternalIdentityProvisioningOptions>? configure = null)
    {
        var options = new ExternalIdentityProvisioningOptions
        {
            GraphBaseUrl = GraphBaseUrl,
            ApiServicePrincipalObjectId = ApiServicePrincipalObjectId,
            InviteRedirectUrl = InviteRedirectUrl,
            TrialAppRoleId = TrialAppRoleId,
            RoadRunnerAppRoleId = RoadRunnerAppRoleId,
            AdminAppRoleId = AdminAppRoleId
        };

        configure?.Invoke(options);

        return new ExternalIdentityProvisioningService(
            httpClient,
            Options.Create(options),
            NullLogger<ExternalIdentityProvisioningService>.Instance,
            new FakeTokenCredential());
    }

    private static IOptions<ExternalIdentityProvisioningOptions> CreateOptions(Action<ExternalIdentityProvisioningOptions>? configure = null)
    {
        var options = new ExternalIdentityProvisioningOptions
        {
            GraphBaseUrl = GraphBaseUrl,
            ApiServicePrincipalObjectId = ApiServicePrincipalObjectId,
            InviteRedirectUrl = InviteRedirectUrl,
            TrialAppRoleId = TrialAppRoleId,
            RoadRunnerAppRoleId = RoadRunnerAppRoleId,
            AdminAppRoleId = AdminAppRoleId
        };

        configure?.Invoke(options);
        return Options.Create(options);
    }

    private static Mock<HttpMessageHandler> CreateOrderedHandler(params ResponsePlan[] plans)
    {
        var remainingPlans = new Queue<ResponsePlan>(plans);
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                remainingPlans.Should().NotBeEmpty("every Graph request in these tests should have a planned response");
                var plan = remainingPlans.Dequeue();

                request.Method.Should().Be(plan.Method);
                request.RequestUri.Should().Be(new Uri(plan.RequestUri));
                request.Headers.Authorization.Should().NotBeNull();
                request.Headers.Authorization!.Scheme.Should().Be("Bearer");
                request.Headers.Authorization.Parameter.Should().Be("fake-token");
                request.Headers.Accept.Should().Contain(header => header.MediaType == "application/json");

                plan.AssertRequest?.Invoke(request);
                return plan.ResponseFactory();
            });

        return handler;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = JsonContent.Create(System.Text.Json.JsonSerializer.Deserialize<object>(json))
        };

    private static HttpResponseMessage EmptyResponse(HttpStatusCode statusCode) => new(statusCode);

    private static ResponsePlan Plan(
        HttpMethod method,
        string requestUri,
        Func<HttpResponseMessage> responseFactory,
        Action<HttpRequestMessage>? assertRequest = null) =>
        new(method, requestUri, responseFactory, assertRequest);

    private static string UsersLookupUrl(string email)
    {
        var escapedEmail = email.Trim().Replace("'", "''", StringComparison.Ordinal);
        var filter = $"mail eq '{escapedEmail}' or userPrincipalName eq '{escapedEmail}' or otherMails/any(candidate:candidate eq '{escapedEmail}')";
        var requestPath = $"users?$filter={Uri.EscapeDataString(filter)}&$select={Uri.EscapeDataString("id,userType,mail,userPrincipalName,otherMails")}&$top=10";
        return $"{GraphBaseUrl}/{requestPath}";
    }

    private static string ServicePrincipalUrl() =>
        $"{GraphBaseUrl}/servicePrincipals/{ApiServicePrincipalObjectId}?$select=id,appRoles";

    private static string UserAssignmentsUrl(string externalDirectoryObjectId) =>
        $"{GraphBaseUrl}/users/{externalDirectoryObjectId}/appRoleAssignments?$select=id,appRoleId,resourceId&$top=100";

    private static string CreateAssignmentUrl() =>
        $"{GraphBaseUrl}/servicePrincipals/{ApiServicePrincipalObjectId}/appRoleAssignedTo";

    private static string DeleteAssignmentUrl(string assignmentId) =>
        $"{GraphBaseUrl}/servicePrincipals/{ApiServicePrincipalObjectId}/appRoleAssignedTo/{assignmentId}";

    private static string ServicePrincipalResponseJson() =>
        $$"""
        {
          "id": "{{ApiServicePrincipalObjectId}}",
          "appRoles": [
            {
              "id": "{{TrialAppRoleId}}",
              "value": "DemoUser",
              "isEnabled": true,
              "allowedMemberTypes": [ "User" ]
            },
            {
              "id": "{{RoadRunnerAppRoleId}}",
              "value": "Roadrunner",
              "isEnabled": true,
              "allowedMemberTypes": [ "User" ]
            },
            {
              "id": "{{AdminAppRoleId}}",
              "value": "mcr-api-admin",
              "isEnabled": true,
              "allowedMemberTypes": [ "User" ]
            }
          ]
        }
        """;

    private static string AssignmentsResponseJson(params string[] assignments) =>
        $$"""
        {
          "value": [
            {{string.Join(",\n    ", assignments)}}
          ]
        }
        """;

    private static string AssignmentJson(string assignmentId, string appRoleId, string resourceId) =>
        $$"""
        {
          "id": "{{assignmentId}}",
          "appRoleId": "{{appRoleId}}",
          "resourceId": "{{resourceId}}"
        }
        """;

    private static void AssertInvitationPayload(
        HttpRequestMessage request,
        string expectedEmail,
        string expectedDisplayName,
        string expectedRedirectUrl,
        bool expectedSendInvitationMessage)
    {
        var payload = request.Content!.ReadFromJsonAsync<Dictionary<string, object?>>().GetAwaiter().GetResult();

        payload.Should().NotBeNull();
        payload!["invitedUserEmailAddress"]?.ToString().Should().Be(expectedEmail);
        payload["invitedUserDisplayName"]?.ToString().Should().Be(expectedDisplayName);
        payload["inviteRedirectUrl"]?.ToString().Should().Be(expectedRedirectUrl);
        payload["sendInvitationMessage"]?.ToString().Should().Be(expectedSendInvitationMessage.ToString());
        payload["invitedUserType"]?.ToString().Should().Be("Guest");
    }

    private static void AssertAssignmentPayload(
        HttpRequestMessage request,
        string expectedPrincipalId,
        string expectedResourceId,
        string expectedAppRoleId)
    {
        var payload = request.Content!.ReadFromJsonAsync<Dictionary<string, object?>>().GetAwaiter().GetResult();

        payload.Should().NotBeNull();
        payload!["principalId"]?.ToString().Should().Be(expectedPrincipalId);
        payload["resourceId"]?.ToString().Should().Be(expectedResourceId);
        payload["appRoleId"]?.ToString().Should().Be(expectedAppRoleId);
    }

    private sealed record ResponsePlan(
        HttpMethod Method,
        string RequestUri,
        Func<HttpResponseMessage> ResponseFactory,
        Action<HttpRequestMessage>? AssertRequest);

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return new AccessToken("fake-token", DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new AccessToken("fake-token", DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
}
