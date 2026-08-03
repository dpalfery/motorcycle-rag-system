using System.Text.RegularExpressions;

namespace MotorcycleRAG.Persistence.Tests.Sql;

/// <summary>
/// Static text-contract tests asserting that the three anchor-column DDL blocks
/// declared by plan
/// <c>6-Docs/archive/plans/2026-08-01-vector-graph-anchor-id-contract.md</c>,
/// decision D4, are present in the deployed <c>schema.sql</c> file (the script
/// that provisions a fresh database, not a migrations directory). The mirror
/// already correct on disk.
/// </summary>
/// <remarks>
/// <para>
/// <b>No database connection needed.</b> This class reads a <c>.sql</c> file as
/// a string and asserts on its text. No SQL Server instance, no
/// <c>Sql__ConnectionString</c> environment variable, no LocalDB/Testcontainers
/// — this runs anywhere <c>dotnet test</c> runs.
/// </para>
/// <para>
/// <b>What a passing test proves:</b> the <c>schema.sql</c> text contains
/// guarded <c>ALTER TABLE ... ADD [ChunkId]</c> and
/// <c>ADD [SourceContentHash]</c> statements, plus a guarded filtered
/// <c>CREATE NONCLUSTERED INDEX [IX_GraphNode_ChunkId] ... WHERE [ChunkId] IS NOT NULL</c>
/// statement. This is the static substitute for schema-idempotency.
/// </para>
/// <para>
/// <b>What a passing test does not prove:</b> that SQL Server accepts this
/// syntax, that the columns/index are actually created, or that a live
/// re-execution is a true no-op. Those guarantees require a CI-reachable SQL
/// Server, which this repository does not yet provision (see plan §7 residual).
/// </para>
/// <para>
/// <b>RED-first context:</b> the <c>schema.sql</c> mirror was already authored
/// and verified correct before this test class existed. Strict RED is not
/// achievable without reverting prior correct work. This class covers an
/// artifact that previously had zero test coverage — it is authored as
/// expected-GREEN coverage closure.
/// </para>
/// </remarks>
public sealed class SchemaSqlAnchorColumnsTests
{
    // ── ChunkId column ───────────────────────────────────────────────────

    private static readonly Regex ChunkIdColumnPattern = new(
        @"ALTER\s+TABLE\s+\[dbo\]\.\[GraphNode\]\s+ADD\s+\[ChunkId\]\s+NVARCHAR\s*\(\s*128\s*\)\s+NULL",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ChunkIdGuardPattern = new(
        @"COL_LENGTH\s*\(\s*'dbo\.GraphNode'\s*,\s*'ChunkId'\s*\)\s+IS\s+NULL",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    // ── SourceContentHash column ─────────────────────────────────────────

    private static readonly Regex SourceContentHashColumnPattern = new(
        @"ALTER\s+TABLE\s+\[dbo\]\.\[GraphNode\]\s+ADD\s+\[SourceContentHash\]\s+NVARCHAR\s*\(\s*128\s*\)\s+NULL",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SourceContentHashGuardPattern = new(
        @"COL_LENGTH\s*\(\s*'dbo\.GraphNode'\s*,\s*'SourceContentHash'\s*\)\s+IS\s+NULL",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    // ── Filtered index IX_GraphNode_ChunkId ──────────────────────────────

    private static readonly Regex ChunkIdIndexPattern = new(
        @"CREATE\s+NONCLUSTERED\s+INDEX\s+\[IX_GraphNode_ChunkId\][\s\S]*?ON\s+\[dbo\]\.\[GraphNode\][\s\S]*?WHERE\s+\[ChunkId\]\s+IS\s+NOT\s+NULL",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ChunkIdIndexGuardPattern = new(
        @"sys\.indexes[\s\S]*?IX_GraphNode_ChunkId[\s\S]*?OBJECT_ID\s*\(\s*N?'?dbo\.GraphNode'?\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    // ═══════════════════════════════════════════════════════════════════════
    // Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SchemaSql_ShouldAddChunkIdColumn_GuardedByColLengthNullCheck()
    {
        var sql = LoadSchemaSql();

        GuardedStatementFound(sql, ChunkIdGuardPattern, ChunkIdColumnPattern)
            .Should().BeTrue(
                "schema.sql must add dbo.GraphNode.ChunkId as NVARCHAR(128) NULL " +
                "(plan decision D4), guarded by an IF COL_LENGTH('dbo.GraphNode','ChunkId') IS NULL " +
                "check so the script is idempotent on re-run");
    }

    [Fact]
    public void SchemaSql_ShouldAddSourceContentHashColumn_GuardedByColLengthNullCheck()
    {
        var sql = LoadSchemaSql();

        GuardedStatementFound(sql, SourceContentHashGuardPattern, SourceContentHashColumnPattern)
            .Should().BeTrue(
                "schema.sql must add dbo.GraphNode.SourceContentHash as NVARCHAR(128) NULL " +
                "(plan decision D4), guarded by an IF COL_LENGTH('dbo.GraphNode','SourceContentHash') IS NULL " +
                "check so the script is idempotent on re-run");
    }

    [Fact]
    public void SchemaSql_ShouldCreateChunkIdFilteredIndex_GuardedBySysIndexesCheck()
    {
        var sql = LoadSchemaSql();

        GuardedStatementFound(sql, ChunkIdIndexGuardPattern, ChunkIdIndexPattern)
            .Should().BeTrue(
                "schema.sql must create a filtered, non-null IX_GraphNode_ChunkId index on " +
                "dbo.GraphNode (WHERE [ChunkId] IS NOT NULL), guarded by an IF NOT EXISTS check " +
                "against sys.indexes so the script is idempotent on re-run");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Splits <paramref name="sql"/> on <c>GO</c> statements, then searches
    /// each batch for a pair of patterns representing a guard condition and
    /// the guarded DDL statement. Returns <c>true</c> when at least one batch
    /// contains BOTH patterns.
    /// </summary>
    /// <remarks>
    /// <c>schema.sql</c> uses single-statement <c>IF ... ALTER</c> guards
    /// (no <c>BEGIN/END</c> blocks), so we can't use the bounded
    /// <c>GuardedBlockPattern</c> from the migration-test sibling. Instead we
    /// split on <c>GO</c> batch terminators and look for both patterns
    /// co-occurring in the same batch.
    /// </remarks>
    private static bool GuardedStatementFound(
        string sql,
        Regex guardPattern,
        Regex statementPattern)
    {
        var batches = Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        return batches.Any(batch =>
            guardPattern.IsMatch(batch) && statementPattern.IsMatch(batch));
    }

    private static string LoadSchemaSql()
    {
        var path = ResolveSchemaSqlFilePath();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "schema.sql not found at expected path. This file is the deployed " +
                "idempotent provisioning script (not a migration) and must exist.",
                path);
        }

        return File.ReadAllText(path);
    }

    private static string ResolveSchemaSqlFilePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidateSolution = Path.Combine(directory.FullName, "MotorcycleRAG.sln");
            if (File.Exists(candidateSolution))
            {
                return Path.Combine(
                    directory.FullName,
                    "4-Persistence",
                    "MotorcycleRAG.Persistence",
                    "Sql",
                    "schema.sql");
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the MotorcycleRAG repository root (MotorcycleRAG.sln) from the test output directory.");
    }
}
