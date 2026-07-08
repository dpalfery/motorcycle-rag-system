namespace MotorcycleRAG.Domain.ValueObjects;

/// <summary>
/// Canonical motorcycle category used for index partitioning and routing.
/// Enforces exactly the four valid values: Dirt, Touring, Sport, Cruiser.
/// </summary>
/// <remarks>
/// The canonical wire value is lowercase ("dirt", "touring", "sport", "cruiser") to
/// match Azure AI Search faceting conventions and the <c>category</c> index field.
/// Parsing is case-insensitive so any casing ("Dirt", "DIRT", "dirt") is accepted.
/// This value object is the single source of truth for the set of valid categories;
/// downstream components (classifier, index routing, query filters) consume this type.
/// </remarks>
public readonly struct MotorcycleCategory : IEquatable<MotorcycleCategory>
{
    private readonly string _value;

    private MotorcycleCategory(string value)
    {
        _value = value;
    }

    /// <summary>
    /// Canonical lowercase wire value ("dirt", "touring", "sport", "cruiser").
    /// Returns <see cref="string.Empty"/> for an uninitialized (<c>default</c>) instance.
    /// </summary>
    public string Value => _value ?? string.Empty;

    /// <summary>
    /// Gets a value indicating whether this instance has been assigned one of the
    /// four valid categories (i.e. it is not the <c>default</c>/empty value).
    /// </summary>
    public bool IsDefined => !string.IsNullOrEmpty(_value);

    /// <summary>Off-road / motocross / enduro motorcycles.</summary>
    public static readonly MotorcycleCategory Dirt = new("dirt");

    /// <summary>Long-distance / touring motorcycles.</summary>
    public static readonly MotorcycleCategory Touring = new("touring");

    /// <summary>High-performance / sport motorcycles.</summary>
    public static readonly MotorcycleCategory Sport = new("sport");

    /// <summary>Cruiser motorcycles.</summary>
    public static readonly MotorcycleCategory Cruiser = new("cruiser");

    /// <summary>
    /// All valid categories in canonical order.
    /// </summary>
    public static IReadOnlyCollection<MotorcycleCategory> All { get; } =
    [
        Dirt,
        Touring,
        Sport,
        Cruiser
    ];

    /// <summary>
    /// Parses a category string (case-insensitive). Throws if the value is not one
    /// of the four valid categories.
    /// </summary>
    /// <param name="value">The category string (e.g. "dirt", "Dirt", "TOURING").</param>
    /// <returns>The matching <see cref="MotorcycleCategory"/>.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is not a valid category.
    /// </exception>
    public static MotorcycleCategory Parse(string? value)
    {
        if (!TryParse(value, out var category))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid MotorcycleCategory. Valid values: dirt, touring, sport, cruiser.",
                nameof(value));
        }

        return category;
    }

    /// <summary>
    /// Attempts to parse a category string (case-insensitive) without throwing.
    /// </summary>
    /// <param name="value">The category string to parse.</param>
    /// <param name="category">The parsed category, or <c>default</c> if parsing failed.</param>
    /// <returns><c>true</c> if the value is a valid category; otherwise <c>false</c>.</returns>
    public static bool TryParse(string? value, out MotorcycleCategory category)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();

        category = normalized switch
        {
            "dirt" => Dirt,
            "touring" => Touring,
            "sport" => Sport,
            "cruiser" => Cruiser,
            _ => default
        };

        return category.IsDefined;
    }

    /// <summary>
    /// Determines whether the supplied instance is one of the four valid categories.
    /// </summary>
    public static bool IsDefinedValue(MotorcycleCategory category) => category.IsDefined;

    /// <inheritdoc />
    public bool Equals(MotorcycleCategory other) =>
        string.Equals(_value ?? string.Empty, other._value ?? string.Empty, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is MotorcycleCategory other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(_value ?? string.Empty);

    /// <inheritdoc />
    public override string ToString() => _value ?? string.Empty;

    public static bool operator ==(MotorcycleCategory left, MotorcycleCategory right) => left.Equals(right);

    public static bool operator !=(MotorcycleCategory left, MotorcycleCategory right) => !left.Equals(right);
}
