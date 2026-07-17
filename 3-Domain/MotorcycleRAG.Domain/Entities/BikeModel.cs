namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Represents a canonical motorcycle model in the system.
/// Supports name normalization (title-case make, uppercase model) and alias matching.
/// Used by the ingestion pipeline to deduplicate and canonicalize bike references.
/// </summary>
/// <remarks>
/// State is encapsulated: identity fields are immutable after construction, and the only
/// mutable state (aliases and the corresponding update timestamp) is changed exclusively
/// through <see cref="UpdateAliases"/>. Use <see cref="Create"/> for new records and
/// <see cref="Rehydrate"/> to rebuild a persisted record at the Persistence boundary.
/// There are no public or init setters.
/// </remarks>
public class BikeModel
{
    private BikeModel(
        Guid id,
        string make,
        string model,
        int year,
        string? aliases,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        string? createdByUserId,
        string? uploadRef)
    {
        Id = id;
        Make = make;
        Model = model;
        Year = year;
        Aliases = aliases;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        CreatedByUserId = createdByUserId;
        UploadRef = uploadRef;
    }

    /// <summary>
    /// Creates a new canonical bike model with a fresh identifier and the current UTC
    /// timestamp on both <see cref="CreatedAtUtc"/> and <see cref="UpdatedAtUtc"/>.
    /// Validates the identity fields used by matching and persistence.
    /// </summary>
    /// <param name="make">Manufacturer name, e.g. "Honda". Trimmed.</param>
    /// <param name="model">Model designation, e.g. "CBR 1000RR". Trimmed.</param>
    /// <param name="year">Production year; must be positive.</param>
    /// <param name="aliases">Optional comma-separated alias variants, e.g. "CBR1000RR,Fireblade". Whitespace-only becomes null.</param>
    /// <param name="createdByUserId">Subject/user ID that produced this record.</param>
    /// <param name="uploadRef">Upload batch reference that produced this record.</param>
    public static BikeModel Create(
        string make,
        string model,
        int year,
        string? aliases = null,
        string? createdByUserId = null,
        string? uploadRef = null) =>
        Rehydrate(
            id: Guid.NewGuid(),
            make,
            model,
            year,
            aliases,
            createdAtUtc: DateTimeOffset.UtcNow,
            updatedAtUtc: DateTimeOffset.UtcNow,
            createdByUserId,
            uploadRef);

    /// <summary>
    /// Rehydrates a persisted bike model after validating the complete identity state at
    /// the Persistence boundary. Invalid database rows are rejected rather than becoming a
    /// partially-valid domain entity.
    /// </summary>
    /// <param name="id">Primary key (GUID) as stored in the database.</param>
    /// <param name="make">Manufacturer name as stored; must be non-empty.</param>
    /// <param name="model">Model designation as stored; must be non-empty.</param>
    /// <param name="year">Production year; must be positive.</param>
    /// <param name="aliases">Stored aliases; whitespace-only becomes null.</param>
    /// <param name="createdAtUtc">UTC creation timestamp as stored.</param>
    /// <param name="updatedAtUtc">UTC last-update timestamp as stored.</param>
    /// <param name="createdByUserId">Subject/user ID that produced this record, if persisted.</param>
    /// <param name="uploadRef">Upload batch reference that produced this record, if persisted.</param>
    public static BikeModel Rehydrate(
        Guid id,
        string make,
        string model,
        int year,
        string? aliases,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        string? createdByUserId,
        string? uploadRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(make);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (year <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be positive.");
        }

        if (id == Guid.Empty)
        {
            throw new ArgumentException("Bike model id is required.", nameof(id));
        }

        return new BikeModel(
            id,
            make.Trim(),
            model.Trim(),
            year,
            NormalizeAliases(aliases),
            createdAtUtc.ToUniversalTime(),
            updatedAtUtc.ToUniversalTime(),
            createdByUserId,
            uploadRef);
    }

    /// <summary>
    /// Replaces the alias list and stamps <see cref="UpdatedAtUtc"/>. Whitespace-only input
    /// clears the aliases (sets them to null).
    /// </summary>
    /// <param name="aliases">New comma-separated alias variants, or null/whitespace to clear.</param>
    public void UpdateAliases(string? aliases)
    {
        Aliases = NormalizeAliases(aliases);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string? NormalizeAliases(string? aliases)
        => string.IsNullOrWhiteSpace(aliases) ? null : aliases.Trim();

    // --- Identity (immutable after construction) ---

    /// <summary>Primary key — GUID assigned at creation time.</summary>
    public Guid Id { get; }

    /// <summary>Manufacturer name, e.g. "Honda".</summary>
    public string Make { get; }

    /// <summary>Model designation, e.g. "CBR 1000RR".</summary>
    public string Model { get; }

    /// <summary>Production year.</summary>
    public int Year { get; }

    /// <summary>UTC timestamp when the record was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>Subject/user ID of the user who created or last uploaded this record.</summary>
    public string? CreatedByUserId { get; }

    /// <summary>Upload reference (e.g. upload batch ID) that produced this record.</summary>
    public string? UploadRef { get; }

    // --- Mutable lifecycle state (read-only publicly; mutated only via UpdateAliases) ---

    /// <summary>Comma-separated alias variants, nullable. E.g. "CBR1000RR,Fireblade".</summary>
    public string? Aliases { get; private set; }

    /// <summary>UTC timestamp when the record was last updated.</summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    // --- Computed projection ---

    /// <summary>Normalized "Make Model" name, e.g. "Honda CBR 1000RR". Computed via <see cref="NormalizeName"/>.</summary>
    public string NormalizedName => string.IsNullOrWhiteSpace(Make) && string.IsNullOrWhiteSpace(Model)
        ? string.Empty
        : NormalizeName($"{Make} {Model}");

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
