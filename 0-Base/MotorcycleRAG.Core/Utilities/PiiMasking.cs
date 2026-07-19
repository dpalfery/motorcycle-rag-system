namespace MotorcycleRAG.Core.Utilities;

/// <summary>
/// Masks personally identifiable information before it reaches logs or telemetry.
/// </summary>
public static class PiiMasking
{
    /// <summary>
    /// Masks an email address for safe inclusion in logs, keeping the first local-part
    /// character and the full domain (e.g. "j***@example.com") so log readers can still
    /// correlate entries without the full address being retained in plain text.
    /// </summary>
    public static string MaskEmailForLog(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1)
            return "[redacted]";

        var localFirstChar = email[0];
        var domain = email[(atIndex + 1)..];
        return $"{localFirstChar}***@{domain}";
    }
}
