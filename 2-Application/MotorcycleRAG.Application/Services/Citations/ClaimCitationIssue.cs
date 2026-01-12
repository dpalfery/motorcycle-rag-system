namespace MotorcycleRAG.Application.Services.Citations;

/// <summary>
/// Claim citation issue.
/// </summary>
public sealed class ClaimCitationIssue
{
    public string Claim { get; set; } = string.Empty;

    public ClaimCitationIssueType IssueType { get; set; }

    public string Suggestion { get; set; } = string.Empty;
}
