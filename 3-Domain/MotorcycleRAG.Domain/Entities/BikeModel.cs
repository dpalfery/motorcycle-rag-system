namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Represents a canonical motorcycle model in the system.
/// Supports name normalization (title-case make, uppercase model) and alias matching.
/// Used by the ingestion pipeline to deduplicate and canonicalize bike references.
/// </summary>
public class BikeModel
{
    /// <summary>Primary key — GUID assigned at creation time.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Manufacturer name, e.g. "Honda".</summary>
    public string Make { get; set; } = string.Empty;

    /// <summary>Model designation, e.g. "CBR 1000RR".</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Production year.</summary>
    public int Year { get; set; }

    /// <summary>Comma-separated alias variants, nullable. E.g. "CBR1000RR,Fireblade".</summary>
    public string? Aliases { get; set; }

    /// <summary>UTC timestamp when the record was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp when the record was last updated.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Normalized "Make Model" name, e.g. "Honda CBR 1000RR". Computed via <see cref="NormalizeName"/>.</summary>
    public string NormalizedName { get; set; } = string.Empty;

    /// <summary>Subject/user ID of the user who created or last uploaded this record.</summary>
    public string? CreatedByUserId { get; set; }

    /// <summary>Upload reference (e.g. upload batch ID) that produced this record.</summary>
    public string? UploadRef { get; set; }

    /// <summary>
    /// Normalizes a raw bike name string.
    /// Title-cases the first token (make), uppercases remaining tokens (model portion).
    /// </summary>
    /// <param name="raw">Raw bike name, e.g. "honda CBR 1000rr".</param>
    /// <returns>Normalized name, e.g. "Honda CBR 1000RR".</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="raw"/> is null.</exception>
    public static string NormalizeName(string? raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var tokens = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        // First token is the make — title-case it
        tokens[0] = TitleCase(tokens[0]);

        // Remaining tokens are the model portion — uppercase them
        for (var i = 1; i < tokens.Length; i++)
        {
            tokens[i] = tokens[i].ToUpperInvariant();
        }

        return string.Join(' ', tokens);
    }

    /// <summary>
    /// Checks whether <paramref name="candidate"/> is an alias of <paramref name="canonical"/>.
    /// Strips all spaces, compares case-insensitively.
    /// </summary>
    /// <param name="candidate">Candidate alias string.</param>
    /// <param name="canonical">Canonical model string.</param>
    /// <returns>True if the two strings match after stripping spaces and lowering case.</returns>
    public static bool IsAlias(string candidate, string canonical)
    {
        if (candidate is null || canonical is null)
        {
            return false;
        }

        var normCandidate = candidate.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        var normCanonical = canonical.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

        return string.Equals(normCandidate, normCanonical, StringComparison.Ordinal);
    }

    /// <summary>
    /// Title-cases a single token: first char upper, rest lower.
    /// </summary>
    private static string TitleCase(string token)
    {
        return char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
    }
}
