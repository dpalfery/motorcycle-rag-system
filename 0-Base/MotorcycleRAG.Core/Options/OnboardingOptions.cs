namespace MotorcycleRAG.Core.Options;

/// <summary>
/// Configuration for onboarding and access-request workflows.
/// </summary>
public class OnboardingOptions {
    /// <summary>
    /// Email address or distribution list used for onboarding approval notifications.
    /// </summary>
    public string ApproverAddress { get; set; } = string.Empty;
}