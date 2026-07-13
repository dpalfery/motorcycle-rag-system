using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Microsoft Graph-backed external identity provisioning and app-role reconciliation.
/// </summary>
public class ExternalIdentityProvisioningService : IExternalIdentityProvisioningService {
    private const string DefaultGraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private const string GraphScope = "https://graph.microsoft.com/.default";

    private readonly HttpClient _httpClient;
    private readonly ILogger<ExternalIdentityProvisioningService> _logger;
    private readonly ExternalIdentityProvisioningOptions _options;
    private readonly TokenCredential _credential;
    private readonly SemaphoreSlim _resourceServicePrincipalLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonSerializerOptions = new(JsonSerializerDefaults.Web);

    private (string ObjectId, Dictionary<string, Guid> AppRoleIdsByName, HashSet<Guid> ManagedRoleIds)? _resourceServicePrincipalCache;

    public ExternalIdentityProvisioningService(
        HttpClient httpClient,
        IOptions<ExternalIdentityProvisioningOptions> options,
        ILogger<ExternalIdentityProvisioningService> logger,
        IAzureCredentialProvider credentialProvider)
        : this(httpClient, options, logger, ResolveGraphCredential(options, credentialProvider)) {
    }

    /// <summary>
    /// Test seam that injects a <see cref="TokenCredential"/> so Graph HTTP flows can be unit-tested
    /// without contacting Azure Identity.
    /// </summary>
    internal ExternalIdentityProvisioningService(
        HttpClient httpClient,
        IOptions<ExternalIdentityProvisioningOptions> options,
        ILogger<ExternalIdentityProvisioningService> logger,
        TokenCredential credential) {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
    }

    public async Task<string> ProvisionApprovedUserAsync(string email, string displayName, TierLabel tier, IdentityProvider provider) {
        if (string.IsNullOrWhiteSpace(email)) {
            throw new ArgumentException("Email cannot be null or empty", nameof(email));
        }

        EnsureResourceServicePrincipalConfigured();

        var normalizedEmail = email.Trim();
        var existingExternalDirectoryObjectId = await FindExistingUserObjectIdAsync(normalizedEmail, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(existingExternalDirectoryObjectId)) {
            _logger.LogInformation(
                "Found existing external identity {ExternalDirectoryObjectId} for {Provider}/{Email}; reconciling tier {Tier}",
                existingExternalDirectoryObjectId,
                provider,
                normalizedEmail,
                tier);

            await ReconcileTierAssignmentsInternalAsync(existingExternalDirectoryObjectId, tier, CancellationToken.None);
            return existingExternalDirectoryObjectId;
        }

        var invitationPayload = BuildInvitationPayload(normalizedEmail, displayName);
        using var invitationResponse = await SendGraphAsync(HttpMethod.Post, "invitations", invitationPayload, CancellationToken.None);
        using var invitationDocument = await ReadJsonDocumentAsync(invitationResponse, CancellationToken.None);
        EnsureSuccessStatusCode(invitationResponse, invitationDocument, "Create invitation");

        var externalDirectoryObjectId = TryGetNestedString(invitationDocument.RootElement, "invitedUser", "id")
            ?? await FindExistingUserObjectIdAsync(normalizedEmail, CancellationToken.None)
            ?? throw new InvalidOperationException(
                $"Microsoft Graph invitation for '{normalizedEmail}' succeeded but no invited user object ID could be resolved.");

        _logger.LogInformation(
            "Provisioned external identity {ExternalDirectoryObjectId} for {Provider}/{Email} with tier {Tier}",
            externalDirectoryObjectId,
            provider,
            normalizedEmail,
            tier);

        await ReconcileTierAssignmentsInternalAsync(externalDirectoryObjectId, tier, CancellationToken.None);
        return externalDirectoryObjectId;
    }

    public async Task ReconcileTierAssignmentsAsync(string externalDirectoryObjectId, TierLabel tier) {
        if (string.IsNullOrWhiteSpace(externalDirectoryObjectId)) {
            throw new ArgumentException("External directory object ID cannot be null or empty", nameof(externalDirectoryObjectId));
        }

        await ReconcileTierAssignmentsInternalAsync(externalDirectoryObjectId.Trim(), tier, CancellationToken.None);
    }

    public async Task RevokeAccessAsync(string externalDirectoryObjectId) {
        if (string.IsNullOrWhiteSpace(externalDirectoryObjectId)) {
            throw new ArgumentException("External directory object ID cannot be null or empty", nameof(externalDirectoryObjectId));
        }

        EnsureResourceServicePrincipalConfigured();

        var resourceServicePrincipal = await GetResourceServicePrincipalAsync(CancellationToken.None);
        var assignments = await ListManagedAssignmentsAsync(externalDirectoryObjectId.Trim(), resourceServicePrincipal, CancellationToken.None);

        foreach (var assignment in assignments) {
            await DeleteManagedAssignmentAsync(resourceServicePrincipal.ObjectId, assignment.AssignmentId, CancellationToken.None);
        }

        _logger.LogInformation(
            "Revoked onboarding-managed external identity access for {ExternalDirectoryObjectId}",
            externalDirectoryObjectId);
    }

    private object BuildInvitationPayload(string email, string displayName) {
        var inviteRedirectUrl = _options.InviteRedirectUrl?.Trim();
        if (string.IsNullOrWhiteSpace(inviteRedirectUrl)) {
            throw new InvalidOperationException(
                "External identity provisioning is not configured. Set ExternalIdentityProvisioning:InviteRedirectUrl in Azure App Configuration.");
        }

        if (!Uri.TryCreate(inviteRedirectUrl, UriKind.Absolute, out _)) {
            throw new InvalidOperationException("ExternalIdentityProvisioning:InviteRedirectUrl must be an absolute URI.");
        }

        var payload = new Dictionary<string, object?> {
            ["invitedUserEmailAddress"] = email,
            ["invitedUserDisplayName"] = string.IsNullOrWhiteSpace(displayName) ? email : displayName.Trim(),
            ["inviteRedirectUrl"] = inviteRedirectUrl,
            ["sendInvitationMessage"] = _options.SendInvitationMessage,
            ["invitedUserType"] = "Guest"
        };

        if (!string.IsNullOrWhiteSpace(_options.InvitationMessageBody) || !string.IsNullOrWhiteSpace(_options.InvitationMessageLanguage)) {
            payload["invitedUserMessageInfo"] = new Dictionary<string, object?> {
                ["customizedMessageBody"] = string.IsNullOrWhiteSpace(_options.InvitationMessageBody) ? null : _options.InvitationMessageBody.Trim(),
                ["messageLanguage"] = string.IsNullOrWhiteSpace(_options.InvitationMessageLanguage) ? null : _options.InvitationMessageLanguage.Trim()
            };
        }

        return payload;
    }

    private void EnsureResourceServicePrincipalConfigured() {
        if (!string.IsNullOrWhiteSpace(_options.ApiServicePrincipalObjectId)) {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiApplicationClientId)) {
            return;
        }

        throw new InvalidOperationException(
            "External identity provisioning is not configured. Set ExternalIdentityProvisioning:ApiServicePrincipalObjectId or ExternalIdentityProvisioning:ApiApplicationClientId in Azure App Configuration.");
    }

    private async Task ReconcileTierAssignmentsInternalAsync(string externalDirectoryObjectId, TierLabel tier, CancellationToken cancellationToken) {
        var resourceServicePrincipal = await GetResourceServicePrincipalAsync(cancellationToken);
        var desiredRoleId = ResolveTargetRoleId(tier, resourceServicePrincipal.AppRoleIdsByName);
        var assignments = await ListManagedAssignmentsAsync(externalDirectoryObjectId, resourceServicePrincipal, cancellationToken);

        var hasDesiredAssignment = false;
        foreach (var assignment in assignments) {
            if (assignment.AppRoleId == desiredRoleId) {
                hasDesiredAssignment = true;
                continue;
            }

            await DeleteManagedAssignmentAsync(resourceServicePrincipal.ObjectId, assignment.AssignmentId, cancellationToken);
        }

        if (!hasDesiredAssignment) {
            await CreateManagedAssignmentAsync(resourceServicePrincipal.ObjectId, externalDirectoryObjectId, desiredRoleId, cancellationToken);
        }

        _logger.LogInformation(
            "Reconciled onboarding-managed app-role assignments for external identity {ExternalDirectoryObjectId} to tier {Tier}",
            externalDirectoryObjectId,
            tier);
    }

    private async Task<(string ObjectId, Dictionary<string, Guid> AppRoleIdsByName, HashSet<Guid> ManagedRoleIds)> GetResourceServicePrincipalAsync(CancellationToken cancellationToken) {
        if (_resourceServicePrincipalCache is { } cached) {
            return cached;
        }

        await _resourceServicePrincipalLock.WaitAsync(cancellationToken);
        try {
            if (_resourceServicePrincipalCache is { } existing) {
                return existing;
            }

            var requestPath = !string.IsNullOrWhiteSpace(_options.ApiServicePrincipalObjectId)
                ? $"servicePrincipals/{_options.ApiServicePrincipalObjectId.Trim()}?$select=id,appRoles"
                : $"servicePrincipals(appId='{EscapeODataString(_options.ApiApplicationClientId.Trim())}')?$select=id,appRoles";

            using var response = await SendGraphAsync(HttpMethod.Get, requestPath, payload: null, cancellationToken);
            using var document = await ReadJsonDocumentAsync(response, cancellationToken);
            EnsureSuccessStatusCode(response, document, "Resolve API enterprise application");

            var objectId = TryGetString(document.RootElement, "id")
                ?? throw new InvalidOperationException("Microsoft Graph did not return an 'id' for the configured API enterprise application.");

            var appRoleIdsByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            if (document.RootElement.TryGetProperty("appRoles", out var appRolesElement) && appRolesElement.ValueKind == JsonValueKind.Array) {
                foreach (var appRoleElement in appRolesElement.EnumerateArray()) {
                    if (!IsEnabledUserAssignableAppRole(appRoleElement)) {
                        continue;
                    }

                    var appRoleId = TryGetGuid(appRoleElement, "id");
                    var roleValue = TryGetString(appRoleElement, "value");
                    if (appRoleId is null || string.IsNullOrWhiteSpace(roleValue)) {
                        continue;
                    }

                    appRoleIdsByName[roleValue] = appRoleId.Value;
                }
            }

            var managedRoleIds = new HashSet<Guid> {
                ResolveTargetRoleId(TierLabel.Trial, appRoleIdsByName),
                ResolveTargetRoleId(TierLabel.RoadRunner, appRoleIdsByName),
                ResolveTargetRoleId(TierLabel.Admin, appRoleIdsByName)
            };

            _resourceServicePrincipalCache = (objectId, appRoleIdsByName, managedRoleIds);
            return _resourceServicePrincipalCache.Value;
        }
        finally {
            _resourceServicePrincipalLock.Release();
        }
    }

    private Guid ResolveTargetRoleId(TierLabel tier, Dictionary<string, Guid> appRoleIdsByName) {
        var configuredRoleId = tier switch {
            TierLabel.Trial => _options.TrialAppRoleId,
            TierLabel.RoadRunner => _options.RoadRunnerAppRoleId,
            TierLabel.Admin => _options.AdminAppRoleId,
            _ => throw new InvalidOperationException($"Unsupported onboarding tier '{tier}'.")
        };

        if (!string.IsNullOrWhiteSpace(configuredRoleId)) {
            if (Guid.TryParse(configuredRoleId.Trim(), out var parsedRoleId)) {
                return parsedRoleId;
            }

            throw new InvalidOperationException(
                $"Configured app role ID '{configuredRoleId}' for tier '{tier}' is not a valid GUID.");
        }

        var configuredRoleName = tier switch {
            TierLabel.Trial => _options.TrialAppRoleName,
            TierLabel.RoadRunner => _options.RoadRunnerAppRoleName,
            TierLabel.Admin => _options.AdminAppRoleName,
            _ => throw new InvalidOperationException($"Unsupported onboarding tier '{tier}'.")
        };

        if (!string.IsNullOrWhiteSpace(configuredRoleName) && appRoleIdsByName.TryGetValue(configuredRoleName.Trim(), out var roleId)) {
            return roleId;
        }

        throw new InvalidOperationException(
            $"No API app role could be resolved for tier '{tier}'. Configure ExternalIdentityProvisioning:{tier}AppRoleId or ensure role '{configuredRoleName}' exists on the API enterprise application.");
    }

    private async Task<List<(string AssignmentId, Guid AppRoleId)>> ListManagedAssignmentsAsync(
        string externalDirectoryObjectId,
        (string ObjectId, Dictionary<string, Guid> AppRoleIdsByName, HashSet<Guid> ManagedRoleIds) resourceServicePrincipal,
        CancellationToken cancellationToken) {
        var requestPath = $"users/{externalDirectoryObjectId}/appRoleAssignments?$select=id,appRoleId,resourceId&$top=100";
        using var response = await SendGraphAsync(HttpMethod.Get, requestPath, payload: null, cancellationToken);
        using var document = await ReadJsonDocumentAsync(response, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound) {
            throw new InvalidOperationException(
                $"External identity '{externalDirectoryObjectId}' was not found in Microsoft Graph.");
        }

        EnsureSuccessStatusCode(response, document, "List user app-role assignments");

        var assignments = new List<(string AssignmentId, Guid AppRoleId)>();
        if (!document.RootElement.TryGetProperty("value", out var valueElement) || valueElement.ValueKind != JsonValueKind.Array) {
            return assignments;
        }

        foreach (var assignmentElement in valueElement.EnumerateArray()) {
            var assignmentId = TryGetString(assignmentElement, "id");
            var appRoleId = TryGetGuid(assignmentElement, "appRoleId");
            var resourceId = TryGetGuid(assignmentElement, "resourceId");

            if (string.IsNullOrWhiteSpace(assignmentId) || appRoleId is null || resourceId is null) {
                continue;
            }

            if (!string.Equals(resourceId.Value.ToString(), resourceServicePrincipal.ObjectId, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!resourceServicePrincipal.ManagedRoleIds.Contains(appRoleId.Value)) {
                continue;
            }

            assignments.Add((assignmentId, appRoleId.Value));
        }

        return assignments;
    }

    private async Task CreateManagedAssignmentAsync(
        string resourceServicePrincipalObjectId,
        string externalDirectoryObjectId,
        Guid appRoleId,
        CancellationToken cancellationToken) {
        var payload = new Dictionary<string, object?> {
            ["principalId"] = externalDirectoryObjectId,
            ["resourceId"] = resourceServicePrincipalObjectId,
            ["appRoleId"] = appRoleId
        };

        using var response = await SendGraphAsync(
            HttpMethod.Post,
            $"servicePrincipals/{resourceServicePrincipalObjectId}/appRoleAssignedTo",
            payload,
            cancellationToken);
        using var document = await ReadJsonDocumentAsync(response, cancellationToken);

        if (response.IsSuccessStatusCode) {
            return;
        }

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict) {
            var resourceServicePrincipal = await GetResourceServicePrincipalAsync(cancellationToken);
            var existingAssignments = await ListManagedAssignmentsAsync(externalDirectoryObjectId, resourceServicePrincipal, cancellationToken);
            if (existingAssignments.Any(assignment => assignment.AppRoleId == appRoleId)) {
                return;
            }
        }

        EnsureSuccessStatusCode(response, document, "Assign API app role");
    }

    private async Task DeleteManagedAssignmentAsync(string resourceServicePrincipalObjectId, string appRoleAssignmentId, CancellationToken cancellationToken) {
        using var response = await SendGraphAsync(
            HttpMethod.Delete,
            $"servicePrincipals/{resourceServicePrincipalObjectId}/appRoleAssignedTo/{appRoleAssignmentId}",
            payload: null,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound || response.IsSuccessStatusCode) {
            return;
        }

        using var document = await ReadJsonDocumentAsync(response, cancellationToken);
        EnsureSuccessStatusCode(response, document, "Delete API app-role assignment");
    }

    private async Task<string?> FindExistingUserObjectIdAsync(string email, CancellationToken cancellationToken) {
        var escapedEmail = EscapeODataString(email.Trim());
        var filter = $"mail eq '{escapedEmail}' or userPrincipalName eq '{escapedEmail}' or otherMails/any(candidate:candidate eq '{escapedEmail}')";
        var requestPath = $"users?$filter={Uri.EscapeDataString(filter)}&$select={Uri.EscapeDataString("id,userType,mail,userPrincipalName,otherMails")}&$top=10";

        using var response = await SendGraphAsync(HttpMethod.Get, requestPath, payload: null, cancellationToken, consistencyLevel: "eventual");
        using var document = await ReadJsonDocumentAsync(response, cancellationToken);
        EnsureSuccessStatusCode(response, document, "Look up invited user");

        if (!document.RootElement.TryGetProperty("value", out var valueElement) || valueElement.ValueKind != JsonValueKind.Array) {
            return null;
        }

        var matches = valueElement
            .EnumerateArray()
            .Select(element => new {
                Id = TryGetString(element, "id"),
                UserType = TryGetString(element, "userType"),
                Mail = TryGetString(element, "mail"),
                UserPrincipalName = TryGetString(element, "userPrincipalName"),
                OtherMails = element.TryGetProperty("otherMails", out var otherMailsElement) && otherMailsElement.ValueKind == JsonValueKind.Array
                    ? otherMailsElement.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray()
                    : Array.Empty<string>()
            })
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Id))
            .ToList();

        var guestMatch = matches.FirstOrDefault(candidate =>
            string.Equals(candidate.UserType, "Guest", StringComparison.OrdinalIgnoreCase) &&
            MatchesEmail(candidate.Mail, candidate.UserPrincipalName, candidate.OtherMails, email));

        if (guestMatch != null) {
            return guestMatch.Id;
        }

        var fallbackMatch = matches.FirstOrDefault(candidate => MatchesEmail(candidate.Mail, candidate.UserPrincipalName, candidate.OtherMails, email));
        return fallbackMatch?.Id;
    }

    private static bool MatchesEmail(string? mail, string? userPrincipalName, IEnumerable<string> otherMails, string email) {
        return string.Equals(mail, email, StringComparison.OrdinalIgnoreCase)
            || string.Equals(userPrincipalName, email, StringComparison.OrdinalIgnoreCase)
            || otherMails.Any(otherMail => string.Equals(otherMail, email, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<HttpResponseMessage> SendGraphAsync(
        HttpMethod method,
        string relativePath,
        object? payload,
        CancellationToken cancellationToken,
        string? consistencyLevel = null) {
        using var request = new HttpRequestMessage(method, BuildGraphRequestUri(relativePath));
        var accessToken = await _credential.GetTokenAsync(new TokenRequestContext([GraphScope]), cancellationToken);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(consistencyLevel)) {
            request.Headers.TryAddWithoutValidation("ConsistencyLevel", consistencyLevel);
        }

        if (payload != null) {
            request.Content = JsonContent.Create(payload, options: _jsonSerializerOptions);
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<JsonDocument> ReadJsonDocumentAsync(HttpResponseMessage response, CancellationToken cancellationToken) {
        if (response.Content is null) {
            return JsonDocument.Parse("{}");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(content) ? JsonDocument.Parse("{}") : JsonDocument.Parse(content);
    }

    private Uri BuildGraphRequestUri(string relativePath) {
        var graphBaseUrl = string.IsNullOrWhiteSpace(_options.GraphBaseUrl)
            ? DefaultGraphBaseUrl
            : _options.GraphBaseUrl.Trim().TrimEnd('/');

        return new Uri($"{graphBaseUrl}/{relativePath.TrimStart('/')}", UriKind.Absolute);
    }

    private static TokenCredential ResolveGraphCredential(
        IOptions<ExternalIdentityProvisioningOptions> options,
        IAzureCredentialProvider credentialProvider) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credentialProvider);

        var provisionOptions = options.Value ?? throw new ArgumentNullException(nameof(options));
        return credentialProvider.GetGraphCredential(provisionOptions);
    }

    private static void EnsureSuccessStatusCode(HttpResponseMessage response, JsonDocument document, string operation) {
        if (response.IsSuccessStatusCode) {
            return;
        }

        var graphCode = TryGetNestedString(document.RootElement, "error", "code");
        var graphMessage = TryGetNestedString(document.RootElement, "error", "message");
        var detail = string.IsNullOrWhiteSpace(graphCode) && string.IsNullOrWhiteSpace(graphMessage)
            ? string.Empty
            : $" Graph error: {graphCode ?? "Unknown"} - {graphMessage ?? "No message provided."}";

        throw new InvalidOperationException(
            $"{operation} failed with Microsoft Graph {(int)response.StatusCode} ({response.StatusCode}).{detail}");
    }

    private static bool IsEnabledUserAssignableAppRole(JsonElement appRoleElement) {
        var isEnabled = appRoleElement.TryGetProperty("isEnabled", out var isEnabledElement)
            && isEnabledElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            && isEnabledElement.GetBoolean();

        if (!isEnabled) {
            return false;
        }

        if (!appRoleElement.TryGetProperty("allowedMemberTypes", out var allowedMemberTypesElement)
            || allowedMemberTypesElement.ValueKind != JsonValueKind.Array) {
            return false;
        }

        return allowedMemberTypesElement.EnumerateArray().Any(memberType =>
            memberType.ValueKind == JsonValueKind.String
            && string.Equals(memberType.GetString(), "User", StringComparison.OrdinalIgnoreCase));
    }

    private static string EscapeODataString(string value) {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string? TryGetString(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var propertyElement) || propertyElement.ValueKind != JsonValueKind.String) {
            return null;
        }

        return propertyElement.GetString();
    }

    private static Guid? TryGetGuid(JsonElement element, string propertyName) {
        var value = TryGetString(element, propertyName);
        return Guid.TryParse(value, out var parsedValue) ? parsedValue : null;
    }

    private static string? TryGetNestedString(JsonElement element, string parentPropertyName, string childPropertyName) {
        if (!element.TryGetProperty(parentPropertyName, out var parentElement) || parentElement.ValueKind != JsonValueKind.Object) {
            return null;
        }

        return TryGetString(parentElement, childPropertyName);
    }
}
