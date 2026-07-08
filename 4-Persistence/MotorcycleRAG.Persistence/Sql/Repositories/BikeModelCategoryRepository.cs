using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Repositories;

namespace MotorcycleRAG.Persistence.Sql.Repositories;

/// <summary>
/// Dapper-based implementation of <see cref="IBikeModelCategoryRepository"/>.
/// </summary>
/// <remarks>
/// &lt;para&gt;All SQL uses parameterized queries — no string concatenation.&lt;/para&gt;
/// &lt;para&gt;&lt;b>Casing boundary (resolves the impedance between the T2 &lt;c&gt;MotorcycleCategory&lt;/c&gt;
/// value-object and the &lt;c&gt;BikeModelCategory&lt;/c&gt; CHECK constraint):&lt;/b&gt; the Domain value object's
/// canonical wire value is lowercase ("dirt", "touring", ...) to match Azure AI Search faceting,
/// while the migration's CHECK constraint stores Title-case ("Dirt", "Touring", ...). This repository
/// is the single place that knows the DB's storage form: it maps any valid casing on write to
/// Title-case, and returns the stored token as-is on read (the value object's case-insensitive
/// &lt;c&gt;TryParse&lt;/c&gt; restores the lowercase canonical at the Application layer).&lt;/para&gt;
/// &lt;para&gt;The MERGE upsert uses &lt;c&gt;WITH (HOLDLOCK)&lt;/c&gt; to prevent the race that can otherwise
/// produce a unique-key violation under concurrent upserts of the same (Make, Model).&lt;/para&gt;
/// </remarks>
public class BikeModelCategoryRepository : IBikeModelCategoryRepository
{
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly ILogger<BikeModelCategoryRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BikeModelCategoryRepository"/>.
    /// </summary>
    /// <param name="connectionFactory">SQL connection factory.</param>
    /// <param name="logger">Logger instance.</param>
    public BikeModelCategoryRepository(
        ISqlConnectionFactory connectionFactory,
        ILogger<BikeModelCategoryRepository> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<string?> GetCategoryAsync(
        string make,
        string model,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(make);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        // Deterministic equality seek on UQ_BikeModelCategory_Make_Model — no LIKE, no graph traversal.
        const string sql = @"
            SELECT TOP(1) [Category]
            FROM [dbo].[BikeModelCategory]
            WHERE [Make] = @Make AND [Model] = @Model;
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            return await connection.QueryFirstOrDefaultAsync<string?>(
                new CommandDefinition(sql, new { Make = make, Model = model }, cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read BikeModelCategory for {Make} {Model}", make, model);
            throw new InvalidOperationException(
                $"Failed to read BikeModelCategory for {make} {model}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(
        string make,
        string model,
        string category,
        string source = "Classifier",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(make);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        // Map any valid casing to the Title-case storage form required by the table's CHECK constraint.
        // Invalid tokens are rejected here (defense-in-depth alongside the DB CHECK and the Domain value object).
        var storageCategory = ToStorageCategory(category);

        // WITH (HOLDLOCK) serializes the key lookup so two concurrent upserts of the same
        // (Make, Model) cannot both miss and then both INSERT (unique-key violation).
        const string sql = @"
            MERGE [dbo].[BikeModelCategory] WITH (HOLDLOCK) AS target
            USING (SELECT @Make AS [Make], @Model AS [Model]) AS source
                ON target.[Make] = source.[Make] AND target.[Model] = source.[Model]
            WHEN MATCHED THEN
                UPDATE SET
                    [Category]     = @Category,
                    [Source]       = @Source,
                    [UpdatedAtUtc] = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT ([Make], [Model], [Category], [Source])
                VALUES (@Make, @Model, @Category, @Source);
        ";

        try
        {
            using var connection = await _connectionFactory.CreateOpenConnectionAsync();
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                Make = make,
                Model = model,
                Category = storageCategory,
                Source = source
            }, cancellationToken: cancellationToken));

            _logger.LogInformation(
                "Upserted BikeModelCategory {Make} {Model} -> {Category} (source={Source})",
                make, model, storageCategory, source);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to upsert BikeModelCategory {Make} {Model} -> {Category}",
                make, model, storageCategory);
            throw new InvalidOperationException(
                $"Failed to upsert BikeModelCategory {make} {model} -> {storageCategory}", ex);
        }
    }

    /// <summary>
    /// Maps a category token of any valid casing to the Title-case storage form required by the
    /// <c>CK_BikeModelCategory_Category</c> CHECK constraint ('Dirt','Touring','Sport','Cruiser').
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if <paramref name="category"/> is not a valid token.</exception>
    private static string ToStorageCategory(string category)
    {
        // Case-insensitive match against the 4 CHECK-constraint tokens; preserve the DB's Title-case form.
        return category.Trim().ToLowerInvariant() switch
        {
            "dirt" => "Dirt",
            "touring" => "Touring",
            "sport" => "Sport",
            "cruiser" => "Cruiser",
            _ => throw new ArgumentException(
                $"'{category}' is not a valid MotorcycleCategory. Valid values: dirt, touring, sport, cruiser.",
                nameof(category))
        };
    }
}
