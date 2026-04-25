using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.IntegrationTests.Api;

public class AccessRequestApiIntegrationTests : IClassFixture<TestWebApplicationFactory> {
    private readonly TestWebApplicationFactory _factory;
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AccessRequestApiIntegrationTests(TestWebApplicationFactory factory) {
        _factory = factory;
    }

    [Fact]
    public async Task CreateAccessRequest_NewRequest_ReturnsAccepted() {
        var requestRepository = new Mock<IAccessRequestRepository>();
        requestRepository
            .Setup(repository => repository.GetByProviderAndEmailAsync("rider@example.com", IdentityProvider.Microsoft))
            .ReturnsAsync((PublicAccessRequestResponse?)null);
        requestRepository
            .Setup(repository => repository.CreateAsync(
                It.Is<CreateAccessRequestRequest>(request => request.Email == "rider@example.com" && request.Provider == IdentityProvider.Microsoft),
                It.IsAny<string>()))
            .ReturnsAsync(new PublicAccessRequestResponse {
                RequestId = Guid.NewGuid().ToString(),
                Email = "rider@example.com",
                Provider = IdentityProvider.Microsoft,
                RequesterVisibleStatus = "PendingReview",
                RequestDecisionState = RequestDecisionState.Pending,
                OnboardingExecutionState = OnboardingExecutionState.NotStarted,
                RowState = UserManagementRowState.PendingApproval,
                RequestedAtUtc = DateTime.UtcNow,
                CorrelationId = "corr-accepted"
            });

        using var factory = _factory.WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                services.AddSingleton(requestRepository.Object);
            });
        });

        using var client = factory.CreateClient();
        using var content = new StringContent(
            """{"email":"Rider@Example.com","provider":"Microsoft"}""",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/access-requests", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var payload = System.Text.Json.JsonSerializer.Deserialize<PublicAccessRequestResponse>(await response.Content.ReadAsStringAsync(), JsonOptions);
        payload.Should().NotBeNull();
        payload!.Email.Should().Be("rider@example.com");
        payload.Provider.Should().Be(IdentityProvider.Microsoft);
    }

    [Fact]
    public async Task CreateAccessRequest_DuplicateRequest_ReturnsConflict() {
        var existingRequest = new PublicAccessRequestResponse {
            RequestId = Guid.NewGuid().ToString(),
            Email = "rider@example.com",
            Provider = IdentityProvider.Google,
            RequesterVisibleStatus = "PendingReview",
            RequestDecisionState = RequestDecisionState.Pending,
            OnboardingExecutionState = OnboardingExecutionState.NotStarted,
            RowState = UserManagementRowState.PendingApproval,
            RequestedAtUtc = DateTime.UtcNow,
            CorrelationId = "corr-existing"
        };

        var requestRepository = new Mock<IAccessRequestRepository>();
        requestRepository
            .Setup(repository => repository.GetByProviderAndEmailAsync("rider@example.com", IdentityProvider.Google))
            .ReturnsAsync(existingRequest);

        using var factory = _factory.WithWebHostBuilder(builder => {
            builder.ConfigureServices(services => {
                services.AddSingleton(requestRepository.Object);
            });
        });

        using var client = factory.CreateClient();
        using var content = new StringContent(
            """{"email":"rider@example.com","provider":"Google"}""",
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/access-requests", content);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = System.Text.Json.JsonSerializer.Deserialize<PublicAccessRequestResponse>(await response.Content.ReadAsStringAsync(), JsonOptions);
        payload.Should().BeEquivalentTo(existingRequest);
        requestRepository.Verify(repository => repository.CreateAsync(It.IsAny<CreateAccessRequestRequest>(), It.IsAny<string>()), Times.Never);
    }
}