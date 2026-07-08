using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Repositories;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.ValueObjects;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Resolves a <see cref="MotorcycleCategory"/> for a (Make, Model) pair via a cache-on-miss flow:
/// authoritative SQL cache -&gt; local LLM -&gt; write-back. Powers the D7 category routing.
/// </summary>
/// <remarks>
/// &lt;para&gt;&lt;b>Flow (D7):&lt;/b&gt;&lt;/para&gt;
/// &lt;list type="number"&gt;
/// &lt;item&gt;&lt;description&gt;Normalize (Make, Model) via &lt;c&gt;BikeModel.NormalizeName&lt;/c&gt; so "honda CBR1000RR"
/// and "Honda CBR1000RR" hit the same authoritative cache row.&lt;/description&gt;&lt;/item&gt;
/// &lt;item&gt;&lt;description&gt;Cache read via &lt;see cref="IBikeModelCategoryRepository.GetCategoryAsync"/&gt;.
/// On hit, parse the stored token (case-insensitive) and return — no LLM call.&lt;/description&gt;&lt;/item&gt;
/// &lt;item&gt;&lt;description&gt;On miss, call the local LLM (Qwen3.5-9B) via &lt;see cref="ILocalChatClient"/&gt;
/// and constrain the output to the 4 valid &lt;see cref="MotorcycleCategory"/&gt; values.&lt;/description&gt;&lt;/item&gt;
/// &lt;item&gt;&lt;description&gt;Write the resolved category back to the authoritative cache (only on a successful
/// LLM-derived classification — never on fallback, so a transient outage cannot poison the cache).&lt;/description&gt;&lt;/item&gt;
/// &lt;item&gt;&lt;description&gt;Best-effort graph write-through (optional, satisfies the D7 graph contract):
/// Motorcycle node -[BELONGS_TO]-&gt; Category node, both with deterministic Ids so the write is idempotent.&lt;/description&gt;&lt;/item&gt;
/// &lt;/list&gt;
/// &lt;para&gt;&lt;b>Resilience (R4):&lt;/b&gt; LLM-unreachable / unparseable output NEVER throws. The configured
/// &lt;see cref="ClassifierOptions.FallbackCategory"/&gt; is returned with a logged warning, and the cache is left
/// untouched so the next call retries the LLM.&lt;/para&gt;
/// </remarks>
public class MotorcycleCategoryClassifier : IMotorcycleCategoryClassifier
{
    /// <summary>Stable namespace UUID used to derive deterministic graph node Ids (UUID v5 style).&lt;/summary&gt;
    private static readonly Guid NamespaceId = new("a4c2b3f1-9d7e-4a1b-8f6e-2c3d4e5f6071");

    private static readonly Regex[] CategoryWordMatchers = BuildCategoryWordMatchers();

    private static readonly string SystemPrompt = BuildSystemPrompt();

    private readonly IBikeModelCategoryRepository _categoryRepository;
    private readonly ILocalChatClient _chatClient;
    private readonly IGraphRepository _graphRepository;
    private readonly IOptions<ClassifierOptions> _options;
    private readonly ILogger<MotorcycleCategoryClassifier> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MotorcycleCategoryClassifier"/>.
    /// </summary>
    /// <param name="categoryRepository">Authoritative (Make, Model) -&gt; Category cache.</param>
    /// <param name="chatClient">Local OpenAI-compatible chat client for cache-miss classification.</param>
    /// <param name="graphRepository">Graph repository for the optional BELONGS_TO write-through.</param>
    /// <param name="options">Classifier options.</param>
    /// <param name="logger">Logger instance.</param>
    public MotorcycleCategoryClassifier(
        IBikeModelCategoryRepository categoryRepository,
        ILocalChatClient chatClient,
        IGraphRepository graphRepository,
        IOptions<ClassifierOptions> options,
        ILogger<MotorcycleCategoryClassifier> logger)
    {
        _categoryRepository = categoryRepository ?? throw new ArgumentNullException(nameof(categoryRepository));
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _graphRepository = graphRepository ?? throw new ArgumentNullException(nameof(graphRepository));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves the <see cref="MotorcycleCategory"/> for the given (Make, Model).
    /// </summary>
    /// <param name="make">Raw manufacturer name — will be normalized.</param>
    /// <param name="model">Raw model designation — will be normalized.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A defined <see cref="MotorcycleCategory"/>. Never throws — LLM failure yields the configured fallback.
    /// </returns>
    public async Task<MotorcycleCategory> ResolveCategoryAsync(
        string make,
        string model,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(make);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        // Step 1: normalize BEFORE any cache/LLM call so equivalent spellings share one cache row.
        // Normalize the COMBINED "{make} {model}" then split, so the (Make, Model) tuple exactly
        // matches BikeModel.NormalizeName's title-case-make + uppercase-model algorithm.
        var (normalizedMake, normalizedModel) = NormalizeMakeAndModel(make, model);

        // Step 2: authoritative cache read (deterministic index seek).
        try
        {
            var cached = await _categoryRepository.GetCategoryAsync(normalizedMake, normalizedModel, cancellationToken)
                .ConfigureAwait(false);

            if (cached is not null &&
                MotorcycleCategory.TryParse(cached, out var cachedCategory) &&
                cachedCategory.IsDefined)
            {
                _logger.LogDebug("Category cache HIT for {Make} {Model} -> {Category}",
                    normalizedMake, normalizedModel, cachedCategory.Value);
                return cachedCategory;
            }
        }
        catch (Exception ex)
        {
            // A cache-read failure is non-fatal: fall through to the LLM path and return the result.
            _logger.LogWarning(ex,
                "Category cache read failed for {Make} {Model}; proceeding to LLM",
                normalizedMake, normalizedModel);
        }

        _logger.LogDebug("Category cache MISS for {Make} {Model}; calling LLM",
            normalizedMake, normalizedModel);

        // Step 3: cache miss -> call the local LLM and constrain to the 4 valid categories.
        var (resolved, fromLlm) = await TryClassifyWithLlmAsync(normalizedMake, normalizedModel, cancellationToken)
            .ConfigureAwait(false);

        // Step 4: write-back ONLY on a successful LLM-derived classification.
        // A fallback value must NOT poison the authoritative cache.
        if (fromLlm)
        {
            await WriteBackToCacheAsync(normalizedMake, normalizedModel, resolved, cancellationToken)
                .ConfigureAwait(false);

            // Step 5: optional graph write-through (best-effort; never fatal).
            await WriteBackToGraphAsync(normalizedMake, normalizedModel, resolved, cancellationToken)
                .ConfigureAwait(false);
        }

        return resolved;
    }

    /// <summary>
    /// Calls the LLM and constrains its output to a valid <see cref="MotorcycleCategory"/>.
    /// Returns <c>(category, fromLlm: false)</c> on any failure — caller applies fallback semantics.
    /// </summary>
    private async Task<(MotorcycleCategory Category, bool FromLlm)> TryClassifyWithLlmAsync(
        string normalizedMake,
        string normalizedModel,
        CancellationToken cancellationToken)
    {
        var fallback = ResolveFallbackCategory();

        try
        {
            var userPrompt = $"Motorcycle: {normalizedMake} {normalizedModel}";
            var raw = await _chatClient.GetChatCompletionAsync(SystemPrompt, userPrompt, cancellationToken)
                .ConfigureAwait(false);

            if (TryParseCategoryResponse(raw, out var parsed) && parsed.IsDefined)
            {
                return (parsed, true);
            }

            _logger.LogWarning(
                "LLM returned unparsable category '{Raw}' for {Make} {Model}; using fallback '{Fallback}'",
                raw, normalizedMake, normalizedModel, fallback.Value);
            return (fallback, false);
        }
        catch (OperationCanceledException)
        {
            throw; // cancellation must propagate
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LLM unreachable for {Make} {Model}; using fallback '{Fallback}'",
                normalizedMake, normalizedModel, fallback.Value);
            return (fallback, false);
        }
    }

    /// <summary>
    /// Writes the resolved category to the authoritative cache. Non-fatal on failure (logged).
    /// </summary>
    private async Task WriteBackToCacheAsync(
        string normalizedMake,
        string normalizedModel,
        MotorcycleCategory category,
        CancellationToken cancellationToken)
    {
        try
        {
            await _categoryRepository.UpsertAsync(
                normalizedMake, normalizedModel, category.Value, source: "Classifier", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Cache write-back failed for {Make} {Model} -> {Category}; classification still returned",
                normalizedMake, normalizedModel, category.Value);
        }
    }

    /// <summary>
    /// Optional graph write-through: a deterministic Motorcycle node and Category node linked by a
    /// BELONGS_TO edge. Best-effort — a graph failure is logged and never surfaces to the caller.
    /// </summary>
    /// <remarks>
    /// Per the R5 decision, the authoritative cache is the new <c>BikeModelCategory</c> table; the graph
    /// holds derived BELONGS_TO relationships for entity/relationship queries. The Motorcycle node is
    /// make-aware and year-agnostic (Name = "{Make} {Model}"), with a deterministic Id so re-classification
    /// upserts the same node rather than creating duplicates.
    /// </remarks>
    private async Task WriteBackToGraphAsync(
        string normalizedMake,
        string normalizedModel,
        MotorcycleCategory category,
        CancellationToken cancellationToken)
    {
        try
        {
            var bikeNodeId = DeterministicGuid("Motorcycle", $"{normalizedMake} {normalizedModel}");
            var categoryNodeId = DeterministicGuid("Category", category.Value);

            var now = DateTimeOffset.UtcNow;

            var bikeNode = new GraphNode
            {
                Id = bikeNodeId,
                Name = $"{normalizedMake} {normalizedModel}",
                Type = "Motorcycle",
                Description = $"{normalizedMake} {normalizedModel} motorcycle (classifier-managed)",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            var categoryNode = new GraphNode
            {
                Id = categoryNodeId,
                Name = category.Value,
                Type = "Category",
                Description = "Motorcycle category (classifier-managed)",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            var edge = new GraphEdge
            {
                FromNodeId = bikeNodeId,
                ToNodeId = categoryNodeId,
                RelationshipType = "BELONGS_TO",
                Weight = 1.0,
                Context = "Classifier-derived category membership",
                CreatedAtUtc = now
            };

            await _graphRepository.UpsertNodeAsync(bikeNode, cancellationToken).ConfigureAwait(false);
            await _graphRepository.UpsertNodeAsync(categoryNode, cancellationToken).ConfigureAwait(false);
            await _graphRepository.UpsertEdgeAsync(edge, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Graph write-through failed for {Make} {Model} -> {Category}; cache is still authoritative",
                normalizedMake, normalizedModel, category.Value);
        }
    }

    /// <summary>
    /// Resolves the configured fallback category, defaulting to <c>Sport</c> if the configured value
    /// is invalid (never throws — R4).
    /// </summary>
    private MotorcycleCategory ResolveFallbackCategory()
    {
        var configured = _options.Value.FallbackCategory;
        if (MotorcycleCategory.TryParse(configured, out var parsed) && parsed.IsDefined)
        {
            return parsed;
        }

        _logger.LogWarning(
            "Configured Classifier:FallbackCategory '{Configured}' is not a valid category; defaulting to 'sport'",
            configured);
        return MotorcycleCategory.Sport;
    }

    /// <summary>
    /// Normalizes the (Make, Model) pair and splits it back into normalized make + normalized model tokens.
    /// <see cref="BikeModel.NormalizeName"/> title-cases the make and uppercases the rest, producing a single
    /// string like "Honda CBR 1000RR". The cache key needs Make and Model as separate columns, so we
    /// take the first token as Make and the remainder as Model.
    /// </summary>
    private static (string Make, string Model) NormalizeMakeAndModel(string make, string model)
    {
        // Re-join then re-split so a model that accidentally contains the make, or vice versa, still
        // produces a stable (Make, Model) tuple matching NormalizeName's algorithm.
        var combined = BikeModel.NormalizeName($"{make} {model}");
        var tokens = combined.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        var normalizedMake = tokens[0];
        var normalizedModel = tokens.Length > 1
            ? string.Join(' ', tokens[1..])
            : string.Empty;
        return (normalizedMake, normalizedModel);
    }

    /// <summary>
    /// Parses the LLM's raw output into a valid <see cref="MotorcycleCategory"/>.
    /// Tries an exact parse first (handles "Sport", "sport", "SPORT"); then scans for a category
    /// token as a whole word (handles "The category is Sport." etc.). Case-insensitive throughout.
    /// </summary>
    private static bool TryParseCategoryResponse(string? raw, out MotorcycleCategory category)
    {
        if (MotorcycleCategory.TryParse(raw, out category) && category.IsDefined)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            category = default;
            return false;
        }

        // Whole-word, case-insensitive scan. CategoryWordMatchers is ordered to match MotorcycleCategory.All.
        var allCategories = MotorcycleCategory.All.ToArray();
        for (var i = 0; i < CategoryWordMatchers.Length; i++)
        {
            if (CategoryWordMatchers[i].IsMatch(raw))
            {
                category = allCategories[i];
                return true;
            }
        }

        category = default;
        return false;
    }

    /// <summary>
    /// Computes a deterministic <see cref="Guid"/> (UUID v5 style) from a type tag + name, so the same
    /// (Type, Name) always maps to the same node Id — making graph upserts idempotent.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "SHA-1 is the RFC 4122 §4.3 algorithm for deterministic UUID v5 generation. This is not a security-sensitive collision-resistance use; it produces a stable node Id for idempotent graph upserts.")]
    private static Guid DeterministicGuid(string type, string name)
    {
        // Per RFC 4122 §4.3: SHA-1 over (namespaceBytes || nameBytes); take the first 16 bytes and set version/variant bits.
        var nameBytes = Encoding.UTF8.GetBytes($"{type}:{name}");
        var namespaceBytes = NamespaceId.ToByteArray();

        var buffer = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, buffer, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, buffer, namespaceBytes.Length, nameBytes.Length);

        var hash = SHA1.HashData(buffer);
        var guidBytes = new byte[16];
        Buffer.BlockCopy(hash, 0, guidBytes, 0, 16);

        // Set version 5 (SHA-1) and variant (RFC 4122) bits.
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // version 5
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // variant 10

        return new Guid(guidBytes);
    }

    private static Regex[] BuildCategoryWordMatchers() => MotorcycleCategory.All
        .Select(c => new Regex($@"\b{Regex.Escape(c.Value)}\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)))
        .ToArray();

    private static string BuildSystemPrompt() =>
        """
        You are a motorcycle classification expert. Classify the given motorcycle into EXACTLY ONE of these four categories:
        - Dirt: off-road, motocross, enduro, dual-sport, trail bikes
        - Touring: long-distance road touring, adventure touring
        - Sport: high-performance sport bikes, supersports, track-oriented
        - Cruiser: cruisers, choppers, low-slung road bikes

        Respond with ONLY the single category name (Dirt, Touring, Sport, or Cruiser).
        Do not include any explanation, punctuation, or additional text.
        """;
}
