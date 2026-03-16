using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Agents.Orchestration;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Coordinates the Foundry OrchestratorAgent run loop: creates a thread, adds the user query,
/// drives the run (dispatching tool calls to <see cref="OrchestratorToolHandlers"/>) until
/// the run completes, and returns the synthesized answer embedded in a single <see cref="SearchResult"/>.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S1200:Split this class into smaller and more specialized ones", Justification = "Orchestrator naturally depends on multiple service types")]
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private const int MaxOrchestratorRounds = 4; // Foundry-enforced limit

    private readonly IReadOnlyList<ISearchAgent> _agents;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly IFoundryAgentRunner _runner;
    private readonly FoundryToolDispatcher _dispatcher;
    private readonly AzureFoundryOptions _options;

    public AgentOrchestrator(
        IEnumerable<ISearchAgent> agents,
        ILogger<AgentOrchestrator> logger,
        IFoundryAgentRunner runner,
        FoundryToolDispatcher dispatcher,
        IOptions<AzureFoundryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(options);

        _agents = agents.ToList();
        _logger = logger;
        _runner = runner;
        _dispatcher = dispatcher;
        _options = options.Value;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Drives the Foundry OrchestratorAgent run:
    /// 1. Create thread + add user query
    /// 2. Create run on <see cref="AzureFoundryOptions.OrchestratorAgentId"/>
    /// 3. On <see cref="AgentRunState.RequiresAction"/> → dispatch tool calls → submit outputs
    /// 4. On <see cref="AgentRunState.Completed"/> → extract final answer
    /// Returns the answer as a single synthetic <see cref="SearchResult"/>.
    /// </remarks>
    public async Task<SearchResult[]> ExecuteSequentialSearchAsync(string query, SearchContext context)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogWarning("ExecuteSequentialSearchAsync called with empty query");
            return Array.Empty<SearchResult>();
        }

        context ??= new SearchContext();

        if (string.IsNullOrWhiteSpace(_options.OrchestratorAgentId))
        {
            _logger.LogError("OrchestratorAgentId is not configured — cannot start Foundry run");
            throw new InvalidOperationException("AzureAI:OrchestratorAgentId is not configured");
        }

        var threadId = await _runner.CreateThreadAsync();

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ThreadId"] = threadId,
            ["SessionId"] = context.SessionId ?? "none"
        });

        _logger.LogInformation("Foundry run started: threadId={ThreadId} agentId={AgentId}", threadId, _options.OrchestratorAgentId);

        try
        {
            await _runner.AddUserMessageAsync(threadId, query);
            var status = await _runner.CreateRunAsync(threadId, _options.OrchestratorAgentId);
            var rounds = 0;

            while (status.State == AgentRunState.RequiresAction && rounds < MaxOrchestratorRounds)
            {
                rounds++;
                _logger.LogDebug(
                    "Orchestrator run {RunId}: RequiresAction (round {Round}/{Max}), {Count} tool calls",
                    status.RunId, rounds, MaxOrchestratorRounds, status.RequiredToolCalls?.Count ?? 0);

                var outputs = await _dispatcher.DispatchAsync(status.RequiredToolCalls ?? [], CancellationToken.None);
                status = await _runner.SubmitToolOutputsAsync(threadId, status.RunId, outputs);
            }

            if (status.State != AgentRunState.Completed)
            {
                _logger.LogError(
                    "Orchestrator run {RunId} ended in state {State} (threadId={ThreadId})",
                    status.RunId, status.State, threadId);
                throw new InvalidOperationException(
                    $"Foundry orchestrator run ended with unexpected state: {status.State}");
            }

            var answer = await _runner.GetLastAssistantMessageAsync(threadId);
            _logger.LogInformation(
                "Foundry run completed: runId={RunId} answerLength={Length}",
                status.RunId, answer.Length);

            return
            [
                new SearchResult
                {
                    Id = status.RunId,
                    Content = answer,
                    RelevanceScore = 1.0f,
                    Source = new SearchSource
                    {
                        AgentType = SearchAgentType.QueryPlanner,
                        SourceName = "Azure AI Foundry OrchestratorAgent",
                        DocumentId = threadId
                    },
                    Metadata = new Dictionary<string, object>
                    {
                        ["FoundryAnswer"] = true,
                        ["ThreadId"] = threadId,
                        ["RunId"] = status.RunId,
                        ["Rounds"] = rounds
                    }
                }
            ];
        }
        finally
        {
            await SafeDeleteThreadAsync(threadId);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// In the Foundry architecture the full answer is produced by
    /// <see cref="ExecuteSequentialSearchAsync"/> and embedded in the returned
    /// <see cref="SearchResult.Content"/> of the single synthesized result.
    /// This method extracts that content, or returns empty string if results are empty.
    /// </remarks>
    public Task<string> GenerateResponseAsync(SearchResult[] results, string originalQuery)
    {
        if (results == null || results.Length == 0)
        {
            return Task.FromResult(string.Empty);
        }

        // The first result from ExecuteSequentialSearchAsync carries the full Foundry answer
        var foundryResult = Array.Find(results,
            r => r.Metadata.TryGetValue("FoundryAnswer", out var v) && v is true);

        if (foundryResult != null)
        {
            return Task.FromResult(foundryResult.Content);
        }

        // Fallback: concatenate result contents (handles unit-test mocks that don't use FoundryAnswer)
        var combined = string.Join("\n\n", results.Select(r => r.Content));
        return Task.FromResult(combined);
    }

    /// <inheritdoc />
    public Task<SearchResult[]> OrchestrateSearchAsync(string query, SearchParameters options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(query))
        {
            _logger.LogWarning("OrchestrateSearchAsync called with empty query");
            return Task.FromResult(Array.Empty<SearchResult>());
        }

        var context = new SearchContext
        {
            Preferences = new SearchPreferences
            {
                MaxResults = options.MaxResults,
                MinRelevanceScore = options.MinRelevanceScore
            }
        };

        return ExecuteSequentialSearchAsync(query, context);
    }

    /// <inheritdoc />
    public IEnumerable<ISearchAgent> GetAvailableAgents() => _agents;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task SafeDeleteThreadAsync(string threadId)
    {
        try
        {
            await _runner.DeleteThreadAsync(threadId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete orchestrator thread {ThreadId}", threadId);
        }
    }
}
