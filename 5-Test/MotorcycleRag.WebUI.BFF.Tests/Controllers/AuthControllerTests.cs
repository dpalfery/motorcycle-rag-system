using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq.Protected;
using MotorcycleRag.WebUI.BFF.Configuration.Services;
using MotorcycleRag.WebUI.BFF.Controllers;

namespace MotorcycleRag.WebUI.BFF.Tests.Controllers;

public class AuthControllerTests {
    private readonly Mock<IHttpClientFactory> _clientFactory = new();
    private readonly Mock<ILogger<AuthController>> _logger = new();

    [Fact]
    public void Constructor_WithNullDependencies_ThrowsArgumentNullException() {
        var nullFactory = () => new AuthController(null!, _logger.Object);
        var nullLogger = () => new AuthController(_clientFactory.Object, null!);

        nullFactory.Should().Throw<ArgumentNullException>();
        nullLogger.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("https://ui.example/dashboard", "https://ui.example/dashboard")]
    public void Login_WithOptionalReturnUrl_ReturnsOidcChallenge(string? input, string expectedRedirect) {
        var controller = CreateController(authenticated: false);

        var result = controller.Login(input is null ? null : new Uri(input));

        var challenge = result.Should().BeOfType<ChallengeResult>().Subject;
        challenge.AuthenticationSchemes.Should().ContainSingle()
            .Which.Should().Be("OpenIdConnect");
        challenge.Properties!.RedirectUri.Should().Be(expectedRedirect);
    }

    [Fact]
    public async Task Logout_WhenCalled_SignsOutBothSchemesAndReturnsOk() {
        var authentication = new Mock<IAuthenticationService>();
        authentication
            .Setup(service => service.SignOutAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<AuthenticationProperties?>()))
            .Returns(Task.CompletedTask);
        var controller = CreateController(authenticated: true, authenticationService: authentication);

        var result = await controller.Logout();

        result.Should().BeOfType<OkObjectResult>();
        authentication.Verify(service => service.SignOutAsync(
            It.IsAny<HttpContext>(), "Cookies", null), Times.Once);
        authentication.Verify(service => service.SignOutAsync(
            It.IsAny<HttpContext>(), "OpenIdConnect", null), Times.Once);
    }

    [Fact]
    public async Task GetUser_WhenSignedOut_ReturnsSignedOutState() {
        var controller = CreateController(authenticated: false);

        var result = await controller.GetUser();

        Json(result).GetProperty("approvalStatus").GetString().Should().Be("SignedOut");
        Json(result).GetProperty("sessionAuthenticated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetUser_WhenTokenMissing_ReturnsUnknownApprovalState() {
        var controller = CreateController(authenticated: true);

        var result = await controller.GetUser();

        Json(result).GetProperty("approvalStatus").GetString().Should().Be("Unknown");
        Json(result).GetProperty("sessionAuthenticated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetUser_WhenTokenIsWhitespace_ReturnsUnknownApprovalState() {
        var controller = CreateController(authenticated: true, accessToken: "   ");

        var result = await controller.GetUser();

        Json(result).GetProperty("approvalStatus").GetString().Should().Be("Unknown");
        Json(result).GetProperty("sessionAuthenticated").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetUser_WhenIdentityIsNull_ReturnsSignedOutState() {
        var controller = CreateController(authenticated: false);
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal();

        var result = await controller.GetUser();

        Json(result).GetProperty("approvalStatus").GetString().Should().Be("SignedOut");
    }

    [Fact]
    public async Task GetUser_WhenApiReturnsPartialJson_OmitsMissingFields() {
        SetupClient(CreateClient(HttpStatusCode.OK, """{"id":"only-id"}"""));
        var controller = CreateController(authenticated: true, accessToken: "token");

        var result = await controller.GetUser();

        var json = Json(result);
        json.GetProperty("managedUserId").GetString().Should().Be("only-id");
        json.GetProperty("email").ValueKind.Should().Be(JsonValueKind.Null);
        json.GetProperty("planName").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetUser_WhenApiReturnsEmptyBodyOnForbidden_UsesDefaultMessage() {
        SetupClient(CreateClient(HttpStatusCode.Forbidden, "   "));
        var controller = CreateController(authenticated: true, accessToken: "token");

        var result = await controller.GetUser();

        Json(result).GetProperty("approvalMessage").GetString()
            .Should().Be("Access has not been approved for this account.");
    }

    [Fact]
    public async Task GetUser_WhenClientBaseAddressMissing_ReturnsUnknownApprovalState() {
        SetupClient(new HttpClient());
        var controller = CreateController(authenticated: true, accessToken: "token");

        var result = await controller.GetUser();

        Json(result).GetProperty("approvalStatus").GetString().Should().Be("Unknown");
    }

    [Fact]
    public async Task GetUser_WhenApiApproves_ReturnsManagedUserDetailsAndBearerToken() {
        HttpRequestMessage? capturedRequest = null;
        SetupClient(CreateClient(
            HttpStatusCode.OK,
            """{"id":"user-42","email":"rider@example.com","planName":7}""",
            request => capturedRequest = request));
        var controller = CreateController(authenticated: true, accessToken: "access-token", name: "Rider");

        var result = await controller.GetUser();

        var json = Json(result);
        json.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        json.GetProperty("managedUserId").GetString().Should().Be("user-42");
        json.GetProperty("email").GetString().Should().Be("rider@example.com");
        json.GetProperty("planName").GetString().Should().Be("7");
        capturedRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("access-token");
    }

    [Fact]
    public async Task GetUser_WhenApiApprovesAndIdentityNameIsMissing_UsesFallbackUserName() {
        SetupClient(CreateClient(HttpStatusCode.OK, """{"id":"user-42"}"""));
        var controller = CreateController(authenticated: true, accessToken: "token", name: null);

        var result = await controller.GetUser();

        Json(result).GetProperty("user").GetString().Should().Be("User");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{"error":"Approval pending"}""", "ApprovalRequired", "Approval pending")]
    [InlineData(HttpStatusCode.Unauthorized, """{"title":"Token rejected"}""", "ApiUnauthorized", "Token rejected")]
    [InlineData(HttpStatusCode.Forbidden, "not-json", "ApprovalRequired", "Access has not been approved for this account.")]
    public async Task GetUser_WhenApiDenies_ReturnsApprovalAwareState(
        HttpStatusCode statusCode,
        string body,
        string expectedStatus,
        string expectedMessage) {
        SetupClient(CreateClient(statusCode, body));
        var controller = CreateController(authenticated: true, accessToken: "token");

        var result = await controller.GetUser();

        var json = Json(result);
        json.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        json.GetProperty("approvalStatus").GetString().Should().Be(expectedStatus);
        json.GetProperty("approvalMessage").GetString().Should().Be(expectedMessage);
    }

    [Fact]
    public async Task GetUser_WhenApiReturnsUnexpectedStatus_ReturnsUnknownState() {
        SetupClient(CreateClient(HttpStatusCode.ServiceUnavailable, ""));
        var controller = CreateController(authenticated: true, accessToken: "token", name: null);

        var result = await controller.GetUser();

        var json = Json(result);
        json.GetProperty("approvalStatus").GetString().Should().Be("Unknown");
        json.GetProperty("user").GetString().Should().Be("User");
    }

    [Fact]
    public async Task GetUser_WhenApiDeniesAndIdentityNameIsMissing_UsesFallbackUserName() {
        SetupClient(CreateClient(HttpStatusCode.Forbidden, """{"error":"Approval pending"}"""));
        var controller = CreateController(authenticated: true, accessToken: "token", name: null);

        var result = await controller.GetUser();

        Json(result).GetProperty("user").GetString().Should().Be("User");
    }

    [Fact]
    public async Task GetUser_WhenClientThrows_ReturnsUnknownStateAndLogsError() {
        SetupClient(CreateThrowingClient());
        var controller = CreateController(authenticated: true, accessToken: "token");

        var result = await controller.GetUser();

        Json(result).GetProperty("approvalStatus").GetString().Should().Be("Unknown");
        _logger.Verify(
            logger => logger.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) =>
                    value.ToString()!.Contains("Failed to resolve approval-aware auth state")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetUser_WhenClientThrowsAndIdentityNameIsMissing_UsesFallbackUserName() {
        SetupClient(CreateThrowingClient());
        var controller = CreateController(authenticated: true, accessToken: "token", name: null);

        var result = await controller.GetUser();

        Json(result).GetProperty("user").GetString().Should().Be("User");
    }

    private AuthController CreateController(
        bool authenticated,
        string? accessToken = null,
        string? name = "Test User",
        Mock<IAuthenticationService>? authenticationService = null) {
        var identity = authenticated
            ? new ClaimsIdentity(
                name is null ? [] : [new Claim(ClaimTypes.Name, name)],
                "TestAuthentication")
            : new ClaimsIdentity();
        var context = new DefaultHttpContext {
            User = new ClaimsPrincipal(identity)
        };

        var authentication = authenticationService ?? new Mock<IAuthenticationService>();
        var properties = new AuthenticationProperties();
        if (accessToken is not null) {
            properties.StoreTokens([new AuthenticationToken {
                Name = "access_token",
                Value = accessToken
            }]);
        }
        var ticket = new AuthenticationTicket(context.User, properties, "TestAuthentication");
        authentication
            .Setup(service => service.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string?>()))
            .ReturnsAsync(AuthenticateResult.Success(ticket));

        context.RequestServices = new ServiceCollection()
            .AddSingleton(authentication.Object)
            .BuildServiceProvider();

        return new AuthController(_clientFactory.Object, _logger.Object) {
            ControllerContext = new ControllerContext {
                HttpContext = context
            }
        };
    }

    private void SetupClient(HttpClient client) {
        _clientFactory
            .Setup(factory => factory.CreateClient(AuthenticationServiceConfiguration.ApprovalStatusHttpClientName))
            .Returns(client);
    }

    private static HttpClient CreateClient(
        HttpStatusCode statusCode,
        string body,
        Action<HttpRequestMessage>? capture = null) {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => capture?.Invoke(request))
            .ReturnsAsync(new HttpResponseMessage(statusCode) {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        return new HttpClient(handler.Object) {
            BaseAddress = new Uri("https://api.example/")
        };
    }

    private static HttpClient CreateThrowingClient() {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network failed"));
        return new HttpClient(handler.Object) {
            BaseAddress = new Uri("https://api.example/")
        };
    }

    private static JsonElement Json(ActionResult result) {
        var value = result.Should().BeOfType<OkObjectResult>().Subject.Value;
        return JsonSerializer.SerializeToElement(value);
    }
}
