namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Configuration for Microsoft Graph-backed external identity provisioning.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1056:URI-like properties should not be strings",
    Justification = "String values keep configuration binding simple for infrastructure options.")]
public class ExternalIdentityProvisioningOptions {
    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

    public string TenantId { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string ManagedIdentityClientId { get; set; } = string.Empty;

    public string ApiServicePrincipalObjectId { get; set; } = string.Empty;

    public string ApiApplicationClientId { get; set; } = string.Empty;

    public string InviteRedirectUrl { get; set; } = string.Empty;

    public bool SendInvitationMessage { get; set; } = true;

    public string InvitationMessageLanguage { get; set; } = string.Empty;

    public string InvitationMessageBody { get; set; } = string.Empty;

    public string TrialAppRoleName { get; set; } = "DemoUser";

    public string TrialAppRoleId { get; set; } = string.Empty;

    public string RoadRunnerAppRoleName { get; set; } = "Roadrunner";

    public string RoadRunnerAppRoleId { get; set; } = string.Empty;

    public string AdminAppRoleName { get; set; } = "mcr-api-admin";

    public string AdminAppRoleId { get; set; } = string.Empty;
}
