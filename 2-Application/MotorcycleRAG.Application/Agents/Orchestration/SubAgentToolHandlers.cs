using Microsoft.Extensions.Logging;
using MotorcycleRAG.Application.Agents;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using System.Text.Json;

namespace MotorcycleRAG.Application.Agents.Orchestration;

/// <summary>
/// I/O-only tool handlers for the three Phi-4 sub-agents (VectorSearch, WebSearch, PDFSearch).
/// Each handler deserialises the tool call's ArgumentsJson, executes the corresponding I/O
/// operation, and returns a JSON-serialised result as <see cref="AgentToolOutput.Output"/>.
/// No LLM calls are made here — all reasoning occurs inside Foundry.
/// </summary>
public sealed class SubAgentToolHandlers
{
    private readonly IAzureSearchClient _searchClient;
    private readonly ITrustedSourcesLoader _trustedSourcesLoader;
    private readonly ILogger<SubAgentToolHandlers> _logger;

    public SubAgentToolHandlers(
        IAzureSearchClient searchClient,
        ITrustedSourcesLoader trustedSourcesLoader,
        ILogger<SubAgentToolHandlers> logger)
    {
        ArgumentNullException.ThrowIfNull(searchClient);
        ArgumentNullException.ThrowIfNull(trustedSourcesLoader);
        ArgumentNullException.ThrowIfNull(logger);
        _searchClient = searchClient;
        _trustedSourcesLoader = trustedSourcesLoader;
        _logger = logger;
    }

    /// <summary>
    /// Registers all sub-agent handlers on the given <paramref name="dispatcher"/>.
    /// </summary>
    public void RegisterOn(FoundryToolDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        dispatcher.RegisterHandler("execute_azure_search", HandleExecuteAzureSearchAsync);
        dispatcher.RegisterHandler("fetch_web_content", HandleFetchWebContentAsync);
        dispatcher.RegisterHandler("score_content", HandleScoreContentAsync);
        dispatcher.RegisterHandler("get_trusted_sources", HandleGetTrustedSourcesAsync);
        dispatcher.RegisterHandler("search_pdf_index", HandleSearchPdfIndexAsync);
    }

    // -------------------------------------------------------------------------
    // execute_azure_search — called by VectorSearchAgent
    // -------------------------------------------------------------------------

    public async Task<AgentToolOutput> HandleExecuteAzureSearchAsync(
        AgentToolCall call, CancellationToken ct)
    {
        var args = ParseArgs(call.ArgumentsJson);
        var query = args.TryGetProperty("query", out var q) ? q.GetString() ?? string.Empty : string.Empty;
        var maxResults = args.TryGetProperty("max_results", out var mr) ? mr.GetInt32() : 10;

        _logger.LogDebug("execute_azure_search: query={Query} max={Max}", query, maxResults);

        var results = await _searchClient.SearchAsync(query, new SearchOptions
        {
            MaxResults = maxResults,
            EnableCaching = true,
            IncludeMetadata = true
        });

        _logger.LogInformation(
            "execute_azure_search completed: query={Query} resultCount={ResultCount}",
            query, results.Length);

        var payload = results.Select(r => new
        {
            id = r.Id,
            content = r.Content,
            score = r.RelevanceScore,
            source = r.Source.SourceName
        });

        return new AgentToolOutput(call.CallId, JsonSerializer.Serialize(payload));
    }

    // -------------------------------------------------------------------------
    // fetch_web_content — called by WebSearchAgent
    // -------------------------------------------------------------------------

    public async Task<AgentToolOutput> HandleFetchWebContentAsync(
        AgentToolCall call, CancellationToken ct)
    {
        var args = ParseArgs(call.ArgumentsJson);
        var url = args.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
        var searchTerm = args.TryGetProperty("search_term", out var st) ? st.GetString() ?? string.Empty : string.Empty;

        _logger.LogDebug("fetch_web_content: url={Url}", url);

        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MotorcycleRAGBot/1.0)");
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            var html = await httpClient.GetStringAsync(new Uri(url), ct);

            var extractor = new WebContentExtractor(_logger);
            var source = new TrustedSourceOptions
            {
                Name = url,
                BaseUrl = new Uri(url),
                SearchUrlTemplate = new Uri(url),
                ContentSelector = "//p|//article|//div[@class='content']",
                CredibilityScore = 0.5f
            };
            var extracted = extractor.ExtractSearchResults(html, searchTerm, source);
            var content = string.Join("\n\n", extracted.Select(r => r.Content));
            var truncated = content[..Math.Min(content.Length, 2000)];

            _logger.LogInformation(
                "fetch_web_content completed: url={Url} contentLength={ContentLength}",
                url, truncated.Length);

            return new AgentToolOutput(call.CallId,
                JsonSerializer.Serialize(new { url, content = truncated }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "fetch_web_content failed for url={Url}", url);
            return new AgentToolOutput(call.CallId,
                JsonSerializer.Serialize(new { url, content = string.Empty, error = ex.Message }));
        }
    }

    // -------------------------------------------------------------------------
    // score_content — called by WebSearchAgent (no LLM: pure tier multiplier logic)
    // -------------------------------------------------------------------------

    public Task<AgentToolOutput> HandleScoreContentAsync(
        AgentToolCall call, CancellationToken ct)
    {
        var args = ParseArgs(call.ArgumentsJson);
        var trustTier = args.TryGetProperty("trust_tier", out var tt) ? tt.GetInt32() : 3;

        // Tier → credibility score (matches data-model.md mapping)
        float score = trustTier switch
        {
            1 => 0.95f,
            2 => 0.80f,
            3 => 0.65f,
            4 => 0.50f,
            5 => 0.35f,
            _ => 0.50f
        };

        _logger.LogDebug("score_content: tier={Tier} → score={Score}", trustTier, score);

        return Task.FromResult(new AgentToolOutput(call.CallId,
            JsonSerializer.Serialize(new { score, tier = trustTier })));
    }

    // -------------------------------------------------------------------------
    // get_trusted_sources — called by WebSearchAgent
    // -------------------------------------------------------------------------

    public async Task<AgentToolOutput> HandleGetTrustedSourcesAsync(
        AgentToolCall call, CancellationToken ct)
    {
        _logger.LogDebug("get_trusted_sources: loading from DB");
        var sources = await _trustedSourcesLoader.LoadAsync(ct);

        _logger.LogInformation(
            "get_trusted_sources completed: sourceCount={SourceCount}",
            sources.Length);

        var payload = sources.Select(s => new
        {
            name = s.Name,
            base_url = s.BaseUrl?.ToString() ?? string.Empty,
            search_url_template = s.SearchUrlTemplate?.ToString() ?? string.Empty,
            content_selector = s.ContentSelector,
            credibility_score = s.CredibilityScore
        });

        return new AgentToolOutput(call.CallId, JsonSerializer.Serialize(payload));
    }

    // -------------------------------------------------------------------------
    // search_pdf_index — called by PDFSearchAgent
    // -------------------------------------------------------------------------

    public async Task<AgentToolOutput> HandleSearchPdfIndexAsync(
        AgentToolCall call, CancellationToken ct)
    {
        var args = ParseArgs(call.ArgumentsJson);
        var query = args.TryGetProperty("query", out var q) ? q.GetString() ?? string.Empty : string.Empty;
        var maxResults = args.TryGetProperty("max_results", out var mr) ? mr.GetInt32() : 5;

        _logger.LogDebug("search_pdf_index: query={Query} max={Max}", query, maxResults);

        var results = await _searchClient.SearchAsync(query, new SearchOptions
        {
            MaxResults = maxResults,
            EnableCaching = true,
            IncludeMetadata = true
        });

        _logger.LogInformation(
            "search_pdf_index completed: query={Query} resultCount={ResultCount}",
            query, results.Length);

        var payload = results.Select(r => new
        {
            id = r.Id,
            content = r.Content,
            score = r.RelevanceScore,
            source = r.Source.SourceName,
            document_id = r.Source.DocumentId
        });

        return new AgentToolOutput(call.CallId, JsonSerializer.Serialize(payload));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static JsonElement ParseArgs(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return JsonDocument.Parse("{}").RootElement;

        try
        {
            return JsonDocument.Parse(argumentsJson).RootElement;
        }
        catch
        {
            return JsonDocument.Parse("{}").RootElement;
        }
    }
}
