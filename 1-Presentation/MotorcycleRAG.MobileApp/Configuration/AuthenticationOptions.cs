namespace MotorcycleRAG.MobileApp.Configuration;

/// <summary>
/// Configuration required to compose the mobile MSAL public client.
/// </summary>
public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string ClientId { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;

    public string[] Scopes { get; set; } = [];
}
