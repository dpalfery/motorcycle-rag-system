using System.Net;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Services.Dtos;
using MotorcycleRAG.Admin.ViewModels;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.Tests.ViewModels;

public class UserManagementViewModelTests {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task InitializeAsync_LoadsRows_AndExposesAllowedActions() {
        using var handler = new StubHttpMessageHandler(request => {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri.Should().NotBeNull();
            request.RequestUri!.AbsolutePath.Should().Be("/api/admin/user-management");
            request.Headers.Authorization?.Scheme.Should().Be("Bearer");
            request.Headers.Authorization?.Parameter.Should().Be("test-token");

            var payload = new UserManagementListResponseDto {
                Page = 1,
                PageSize = 100,
                TotalCount = 2,
                Rows = [
                    new UserManagementRowDto {
                        RowId = "row-request",
                        RowType = "AccessRequest",
                        AccessRequestId = "request-1",
                        Email = "pending@example.com",
                        Provider = IdentityProvider.Microsoft,
                        RowState = UserManagementRowState.PendingApproval,
                        RequestDecisionState = RequestDecisionState.Pending,
                        OnboardingExecutionState = OnboardingExecutionState.NotStarted,
                        ManagedUserAccessState = ManagedUserAccessState.None,
                        AllowedActions = ["Approve", "Cancel"],
                        RowVersion = "rv-1"
                    },
                    new UserManagementRowDto {
                        RowId = "row-user",
                        RowType = "ManagedUser",
                        ManagedUserId = "user-1",
                        Email = "active@example.com",
                        Provider = IdentityProvider.Google,
                        AssignedTier = TierLabel.RoadRunner,
                        RowState = UserManagementRowState.Active,
                        RequestDecisionState = RequestDecisionState.Approved,
                        OnboardingExecutionState = OnboardingExecutionState.Completed,
                        ManagedUserAccessState = ManagedUserAccessState.Active,
                        AllowedActions = ["ChangeTier", "Cancel"],
                        RowVersion = "rv-2"
                    }
                ]
            };

            return Task.FromResult(CreateJsonResponse(payload));
        });

        using var harness = CreateHarness(handler);
        var viewModel = harness.ViewModel;

        await viewModel.InitializeAsync();

        viewModel.Rows.Should().HaveCount(2);
        viewModel.Rows[0].CanApprove.Should().BeTrue();
        viewModel.Rows[0].CanCancel.Should().BeTrue();
        viewModel.Rows[1].CanChangeTier.Should().BeTrue();
        viewModel.Rows[1].CanCancel.Should().BeTrue();
        viewModel.SummaryText.Should().Be("Showing 2 of 2 rows");
    }

    [Fact]
    [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling", Justification = "This integration-style view model test constructs a realistic approval response and request payload in one scenario.")]
    public async Task ApproveAsync_SendsSelectedTier_AndAppliesUpdatedRow() {
        string? capturedBody = null;
        using var handler = new StubHttpMessageHandler(async request => {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().NotBeNull();
            request.RequestUri!.AbsolutePath.Should().Be("/api/admin/access-requests/request-1/approve");
            capturedBody = await request.Content!.ReadAsStringAsync();

            return CreateJsonResponse(new AdminActionResponseDto {
                Row = new UserManagementRowDto {
                    RowId = "row-request",
                    RowType = "AccessRequest",
                    AccessRequestId = "request-1",
                    Email = "pending@example.com",
                    Provider = IdentityProvider.Microsoft,
                    AssignedTier = TierLabel.RoadRunner,
                    RowState = UserManagementRowState.OnboardingInProgress,
                    RequestDecisionState = RequestDecisionState.Approved,
                    OnboardingExecutionState = OnboardingExecutionState.InProgress,
                    ManagedUserAccessState = ManagedUserAccessState.None,
                    AllowedActions = ["RetryOnboarding"],
                    RowVersion = "rv-2"
                }
            });
        });

        var successMessages = new List<string>();
        using var harness = CreateHarness(handler, successMessages: successMessages);
        var viewModel = harness.ViewModel;
        var row = new UserManagementRowViewModel(new UserManagementRowDto {
            RowId = "row-request",
            RowType = "AccessRequest",
            AccessRequestId = "request-1",
            Email = "pending@example.com",
            Provider = IdentityProvider.Microsoft,
            RowState = UserManagementRowState.PendingApproval,
            RequestDecisionState = RequestDecisionState.Pending,
            OnboardingExecutionState = OnboardingExecutionState.NotStarted,
            ManagedUserAccessState = ManagedUserAccessState.None,
            AllowedActions = ["Approve"],
            RowVersion = "rv-1"
        }) {
            SelectedTier = TierLabel.RoadRunner
        };

        await viewModel.ApproveAsync(row);

        row.AssignedTier.Should().Be(TierLabel.RoadRunner);
        row.RowState.Should().Be(UserManagementRowState.OnboardingInProgress);
        row.CanRetry.Should().BeTrue();
        successMessages.Should().ContainSingle(message => message == "Access approved.");

        capturedBody.Should().NotBeNullOrWhiteSpace();
        using var document = JsonDocument.Parse(capturedBody!);
        document.RootElement.GetProperty("tier").GetString().Should().Be("RoadRunner");
        document.RootElement.GetProperty("expectedRowVersion").GetString().Should().Be("rv-1");
    }

    [Fact]
    public async Task ApproveAsync_WhenConflict_RefreshesRows_AndShowsApiErrorMessage() {
        var requestCount = 0;
        using var handler = new StubHttpMessageHandler(request => {
            requestCount++;

            return Task.FromResult(requestCount switch {
                1 => CreateJsonResponse(new UserManagementListResponseDto {
                    Page = 1,
                    PageSize = 100,
                    TotalCount = 1,
                    Rows = [
                        new UserManagementRowDto {
                            RowId = "row-request",
                            RowType = "AccessRequest",
                            AccessRequestId = "request-1",
                            Email = "pending@example.com",
                            Provider = IdentityProvider.Microsoft,
                            AssignedTier = TierLabel.Trial,
                            RowState = UserManagementRowState.PendingApproval,
                            RequestDecisionState = RequestDecisionState.Pending,
                            OnboardingExecutionState = OnboardingExecutionState.NotStarted,
                            ManagedUserAccessState = ManagedUserAccessState.None,
                            AllowedActions = ["Approve", "Cancel"],
                            RowVersion = "rv-1"
                        }
                    ]
                }),
                2 => CreateJsonResponse(
                    new { error = "Access request is no longer pending approval. Refresh and try again." },
                    HttpStatusCode.Conflict),
                3 => CreateJsonResponse(new UserManagementListResponseDto {
                    Page = 1,
                    PageSize = 100,
                    TotalCount = 1,
                    Rows = [
                        new UserManagementRowDto {
                            RowId = "row-request",
                            RowType = "AccessRequest",
                            AccessRequestId = "request-1",
                            Email = "pending@example.com",
                            Provider = IdentityProvider.Microsoft,
                            AssignedTier = TierLabel.Trial,
                            RowState = UserManagementRowState.OnboardingInProgress,
                            RequestDecisionState = RequestDecisionState.Approved,
                            OnboardingExecutionState = OnboardingExecutionState.InProgress,
                            ManagedUserAccessState = ManagedUserAccessState.None,
                            AllowedActions = ["RetryOnboarding"],
                            RowVersion = "rv-2"
                        }
                    ]
                }),
                _ => throw new InvalidOperationException($"Unexpected request #{requestCount}: {request.Method} {request.RequestUri}")
            });
        });

        var errorMessages = new List<string>();
        using var harness = CreateHarness(handler, errorMessages: errorMessages);
        var viewModel = harness.ViewModel;

        await viewModel.InitializeAsync();
        viewModel.Rows.Should().HaveCount(1);

        await viewModel.ApproveAsync(viewModel.Rows[0]);

        requestCount.Should().Be(3);
        viewModel.ErrorMessage.Should().Be("Access request is no longer pending approval. Refresh and try again.");
        errorMessages.Should().ContainSingle(message => message == "Access request is no longer pending approval. Refresh and try again.");

        viewModel.Rows.Should().HaveCount(1);
        viewModel.Rows[0].RowState.Should().Be(UserManagementRowState.OnboardingInProgress);
        viewModel.Rows[0].OnboardingExecutionState.Should().Be(OnboardingExecutionState.InProgress);
        viewModel.Rows[0].CanApprove.Should().BeFalse();
        viewModel.Rows[0].CanRetry.Should().BeTrue();
    }

    [Fact]
    public async Task RetryOnboardingAsync_UsesCurrentRowVersion_AndUpdatesState() {
        string? capturedBody = null;
        using var handler = new StubHttpMessageHandler(async request => {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().NotBeNull();
            request.RequestUri!.AbsolutePath.Should().Be("/api/admin/access-requests/request-2/retry-onboarding");
            capturedBody = await request.Content!.ReadAsStringAsync();

            return CreateJsonResponse(new AdminActionResponseDto {
                Row = new UserManagementRowDto {
                    RowId = "row-request",
                    RowType = "AccessRequest",
                    AccessRequestId = "request-2",
                    Email = "failed@example.com",
                    Provider = IdentityProvider.Google,
                    AssignedTier = TierLabel.Trial,
                    RowState = UserManagementRowState.OnboardingInProgress,
                    RequestDecisionState = RequestDecisionState.Approved,
                    OnboardingExecutionState = OnboardingExecutionState.InProgress,
                    ManagedUserAccessState = ManagedUserAccessState.None,
                    AllowedActions = ["Cancel"],
                    RowVersion = "rv-4"
                }
            });
        });

        using var harness = CreateHarness(handler);
        var viewModel = harness.ViewModel;
        var row = new UserManagementRowViewModel(new UserManagementRowDto {
            RowId = "row-request",
            RowType = "AccessRequest",
            AccessRequestId = "request-2",
            Email = "failed@example.com",
            Provider = IdentityProvider.Google,
            AssignedTier = TierLabel.Trial,
            RowState = UserManagementRowState.OnboardingFailed,
            RequestDecisionState = RequestDecisionState.Approved,
            OnboardingExecutionState = OnboardingExecutionState.Failed,
            ManagedUserAccessState = ManagedUserAccessState.None,
            AllowedActions = ["RetryOnboarding"],
            RowVersion = "rv-3"
        });

        await viewModel.RetryOnboardingAsync(row);

        row.RowState.Should().Be(UserManagementRowState.OnboardingInProgress);
        row.OnboardingExecutionState.Should().Be(OnboardingExecutionState.InProgress);

        capturedBody.Should().NotBeNullOrWhiteSpace();
        using var document = JsonDocument.Parse(capturedBody!);
        document.RootElement.GetProperty("expectedRowVersion").GetString().Should().Be("rv-3");
    }

    [Fact]
    public async Task ChangeTierAsync_SendsTierAndReason_AndAppliesUpdatedRow() {
        string? capturedBody = null;
        using var handler = new StubHttpMessageHandler(async request => {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri.Should().NotBeNull();
            request.RequestUri!.AbsolutePath.Should().Be("/api/admin/users/user-9/change-tier");
            capturedBody = await request.Content!.ReadAsStringAsync();

            return CreateJsonResponse(new AdminActionResponseDto {
                Row = new UserManagementRowDto {
                    RowId = "row-user",
                    RowType = "ManagedUser",
                    ManagedUserId = "user-9",
                    Email = "rider@example.com",
                    Provider = IdentityProvider.Microsoft,
                    AssignedTier = TierLabel.Admin,
                    RowState = UserManagementRowState.Active,
                    RequestDecisionState = RequestDecisionState.Approved,
                    OnboardingExecutionState = OnboardingExecutionState.Completed,
                    ManagedUserAccessState = ManagedUserAccessState.Active,
                    AllowedActions = ["ChangeTier", "Cancel"],
                    RowVersion = "rv-10"
                }
            });
        });

        using var harness = CreateHarness(handler);
        var viewModel = harness.ViewModel;
        var row = new UserManagementRowViewModel(new UserManagementRowDto {
            RowId = "row-user",
            RowType = "ManagedUser",
            ManagedUserId = "user-9",
            Email = "rider@example.com",
            Provider = IdentityProvider.Microsoft,
            AssignedTier = TierLabel.RoadRunner,
            RowState = UserManagementRowState.Active,
            RequestDecisionState = RequestDecisionState.Approved,
            OnboardingExecutionState = OnboardingExecutionState.Completed,
            ManagedUserAccessState = ManagedUserAccessState.Active,
            AllowedActions = ["ChangeTier"],
            RowVersion = "rv-9"
        }) {
            SelectedTier = TierLabel.Admin,
            ActionReason = "Elevated for admin coverage"
        };

        await viewModel.ChangeTierAsync(row);

        row.AssignedTier.Should().Be(TierLabel.Admin);
        capturedBody.Should().NotBeNullOrWhiteSpace();
        using var document = JsonDocument.Parse(capturedBody!);
        document.RootElement.GetProperty("tier").GetString().Should().Be("Admin");
        document.RootElement.GetProperty("expectedRowVersion").GetString().Should().Be("rv-9");
        document.RootElement.GetProperty("reason").GetString().Should().Be("Elevated for admin coverage");
    }

    [Fact]
    public async Task CancelAsync_RequiresReason_BeforeCallingApi() {
        var callCount = 0;
        using var handler = new StubHttpMessageHandler(_ => {
            callCount++;
            return Task.FromResult(CreateJsonResponse(new AdminActionResponseDto()));
        });

        var errorMessages = new List<string>();
        using var harness = CreateHarness(handler, errorMessages: errorMessages);
        var viewModel = harness.ViewModel;
        var row = new UserManagementRowViewModel(new UserManagementRowDto {
            RowId = "row-request",
            RowType = "AccessRequest",
            AccessRequestId = "request-5",
            Email = "pending@example.com",
            Provider = IdentityProvider.Microsoft,
            RowState = UserManagementRowState.PendingApproval,
            RequestDecisionState = RequestDecisionState.Pending,
            OnboardingExecutionState = OnboardingExecutionState.NotStarted,
            ManagedUserAccessState = ManagedUserAccessState.None,
            AllowedActions = ["Cancel"],
            RowVersion = "rv-7"
        });

        await viewModel.CancelAsync(row);

        callCount.Should().Be(0);
        viewModel.ErrorMessage.Should().Be("A cancellation reason is required.");
        errorMessages.Should().BeEmpty();
    }

    private static ViewModelHarness CreateHarness(
        HttpMessageHandler handler,
        List<string>? successMessages = null,
        List<string>? errorMessages = null) {
        var authService = new TestAdminAuthService();
        var configService = new TestConfigurationStateService();

        var httpClient = new HttpClient(handler, disposeHandler: false) {
            BaseAddress = new Uri("https://admin.test/")
        };

        var apiClient = new ApiClient(
            httpClient,
            authService,
            Mock.Of<ILogger<ApiClient>>());

        var viewModel = new UserManagementViewModel(
            apiClient,
            authService,
            configService,
            Mock.Of<ILogger<UserManagementViewModel>>(),
            runOnMainThreadAsync: action => action(),
            runOffMainThreadAsync: action => action(),
            showWarningAsync: static (_, _) => Task.CompletedTask,
            showSuccessAsync: (_, message) => {
                successMessages?.Add(message);
                return Task.CompletedTask;
            },
            showErrorAsync: (_, message) => {
                errorMessages?.Add(message);
                return Task.CompletedTask;
            },
            showConfirmAsync: static (_, _, _, _) => Task.FromResult(true));

        return new ViewModelHarness(viewModel, httpClient);
    }

    private static HttpResponseMessage CreateJsonResponse<T>(T payload, HttpStatusCode statusCode = HttpStatusCode.OK) {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return new HttpResponseMessage(statusCode) {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            return _handler(request);
        }
    }

    private sealed class ViewModelHarness : IDisposable {
        private readonly HttpClient _httpClient;

        public ViewModelHarness(UserManagementViewModel viewModel, HttpClient httpClient) {
            ViewModel = viewModel;
            _httpClient = httpClient;
        }

        public UserManagementViewModel ViewModel { get; }

        public void Dispose() {
            _httpClient.Dispose();
        }
    }

    private sealed class TestAdminAuthService : IAdminAuthService {
        public Task<bool> SignInAsync() => Task.FromResult(true);

        public Task SignOutAsync() => Task.CompletedTask;

        public Task<string?> GetAccessTokenAsync() => Task.FromResult<string?>("test-token");

        public bool IsSignedIn() => true;

        public string? UserDisplayName => "Test Admin";

        public Task<IEnumerable<string>> GetUserRolesAsync() => Task.FromResult<IEnumerable<string>>(["mcr-api-admin"]);

        public Task<bool> IsAuthorizedAdminAsync() => Task.FromResult(true);

        public bool IsAuthenticated => true;

        public string? LastAuthErrorMessage => null;
    }

    private sealed class TestConfigurationStateService : IConfigurationStateService {
        public bool IsConfigured => true;

        public bool IsApiConfigured => true;

        public bool IsAuthConfigured => true;

        public Uri? ApiBaseUrl => new("https://admin.test/");

        public string? AuthClientId => "client";

        public string? AuthAuthority => "authority";

        public string? AuthScope => "scope";

        public string? EmbeddingProviderEndpoint => null;

        public string? EmbeddingModel => null;

        public Uri? LocalProcessorEndpoint => null;

        public string? LocalProcessorWorkingDirectory => null;

        public int PdfChunkerMaxTokens => 512;

        public int CsvChunkMaxTokens => 512;

        public string? PdfChunkerTokenizer => "BAAI/bge-small-en-v1.5";

        public string? LocalProcessorUploadJobSecret => null;

        public bool IsLocalProcessorConfigured => false;

#pragma warning disable CS0067
        public event EventHandler? ConfigurationChanged;
#pragma warning restore CS0067

        public Task LoadConfigurationAsync() => Task.CompletedTask;

        public Task SaveApiBaseUrlAsync(Uri? url) => Task.CompletedTask;

        public Task SaveAuthConfigurationAsync(string clientId, string authority, string scope) => Task.CompletedTask;

        public Task SaveEmbeddingConfigurationAsync(string? providerEndpoint, string? model) => Task.CompletedTask;

        public Task SaveLocalProcessorConfigurationAsync(
            Uri? endpoint,
            string? workingDirectory,
            string? uploadJobSecret,
            int pdfChunkerMaxTokens,
            int csvChunkMaxTokens,
            string? pdfChunkerTokenizer) => Task.CompletedTask;

        public Task ClearConfigurationAsync() => Task.CompletedTask;
    }
}
