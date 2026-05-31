using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Application.Services.Agents.Orchestration;
using MotorcycleRAG.Application.Services.QueryValidation;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Coordinates the Foundry OrchestratorAgent response loop: creates a conversation,
/// drives responses (dispatching tool calls to <see cref="OrchestratorToolHandlers"/>) until
/// the response completes, and returns the synthesized answer embedded in a single <see cref="SearchResult"/>.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S1200:Split this class into smaller and more specialized ones", Justification = "Orchestrator naturally depends on multiple service types")]
public sealed class AgentOrchestrator : IAgentOrchestrator
{
    private const int MaxOrchestratorRounds = 10; // Foundry-enforced limit is typically higher, increasing to 10 for deep graph queries

    private readonly IReadOnlyList<ISearchAgent> _agents;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly IFoundryAgentRunner _runner;
    private readonly FoundryToolDispatcher _dispatcher;
    private readonly AzureFoundryOptions _options;
    private readonly ICorrelationService _correlationService;
    private readonly QuestionValidationState _questionValidationState;

    public AgentOrchestrator(
        IEnumerable<ISearchAgent> agents,
        ILogger<AgentOrchestrator> logger,
        IFoundryAgentRunner runner,
        FoundryToolDispatcher dispatcher,
        IOptions<AzureFoundryOptions> options,
        ICorrelationService correlationService,
        QuestionValidationState questionValidationState)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(correlationService);
        ArgumentNullException.ThrowIfNull(questionValidationState);

        _agents = agents.ToList();
        _logger = logger;
        _runner = runner;
        _dispatcher = dispatcher;
        _options = options.Value;
        _correlationService = correlationService;
        _questionValidationState = questionValidationState;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Drives the Foundry OrchestratorAgent response loop:
    /// 1. Create conversation
    /// 2. Send user query to <see cref="AzureFoundryOptions.OrchestratorAgentName"/>
    /// 3. On <see cref="AgentRunState.RequiresAction"/> → dispatch function tool calls → submit outputs
    /// 4. On <see cref="AgentRunState.Completed"/> → return final answer text
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
        _questionValidationState.Initialize(query, context.QueryContext?.RecentMessages);

        if (string.IsNullOrWhiteSpace(_options.OrchestratorAgentName))
        {
            _logger.LogError("OrchestratorAgentName is not configured — cannot start Foundry response");
            throw new InvalidOperationException("AzureAI:OrchestratorAgentName is not configured");
        }

        var conversationId = await _runner.CreateConversationAsync();

        using var scope = _correlationService.CreateLoggingScope(new Dictionary<string, object>
        {
            ["ConversationId"] = conversationId,
            ["SessionId"] = context.SessionId ?? "none"
        });

        _logger.LogInformation("Foundry response started: conversationId={ConversationId} agentName={AgentName}", conversationId, _options.OrchestratorAgentName);

        try
        {
            var status = await _runner.SendAgentMessageAsync(
                conversationId,
                _options.OrchestratorAgentName,
                BuildOrchestratorMessage(query, context.QueryContext?.RecentMessages));
            var rounds = 0;

            while (status.State == AgentRunState.RequiresAction && rounds < MaxOrchestratorRounds)
            {
                rounds++;
                _logger.LogDebug(
                    "Orchestrator response {ResponseId}: RequiresAction (round {Round}/{Max}), {Count} tool calls",
                    status.ResponseId, rounds, MaxOrchestratorRounds, status.RequiredToolCalls?.Count ?? 0);

                var outputs = await _dispatcher.DispatchAsync(status.RequiredToolCalls ?? [], CancellationToken.None);
                status = await _runner.SubmitToolOutputsAsync(conversationId, _options.OrchestratorAgentName, outputs);
            }

            if (status.State != AgentRunState.Completed)
            {
                _logger.LogError(
                    "Orchestrator response {ResponseId} ended in state {State} (conversationId={ConversationId})",
                    status.ResponseId, status.State, conversationId);
                throw new InvalidOperationException(
                    $"Foundry orchestrator response ended with unexpected state: {status.State}");
            }

            _logger.LogInformation(
                "Foundry response completed: responseId={ResponseId} answerLength={Length}",
                status.ResponseId, status.OutputText.Length);

            return
            [
                new SearchResult
                {
                    Id = status.ResponseId,
                    Content = status.OutputText,
                    RelevanceScore = 1.0f,
                    Source = new SearchSource
                    {
                        AgentType = SearchAgentType.QueryPlanner,
                        SourceName = "Azure AI Foundry OrchestratorAgent",
                        DocumentId = conversationId
                    },
                    Metadata = new Dictionary<string, object>
                    {
                        ["FoundryAnswer"] = true,
                        ["ConversationId"] = conversationId,
                        ["ResponseId"] = status.ResponseId,
                        ["Rounds"] = rounds
                    }
                }
            ];
        }
        finally
        {
            await SafeDeleteConversationAsync(conversationId);
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

    private async Task SafeDeleteConversationAsync(string conversationId)
    {
        try
        {
            await _runner.DeleteConversationAsync(conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete orchestrator conversation {ConversationId}", conversationId);
        }
    }

    private static string BuildOrchestratorMessage(
        string query,
        IReadOnlyCollection<QueryRecentMessage>? recentMessages)
    {
        if (recentMessages == null || recentMessages.Count == 0)
        {
            return $"Current user query:\n{query}";
        }

        var clippedMessages = recentMessages
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(6)
            .Select(m => $"{m.Role}: {Clip(m.Content, 1000)}");

        return $"""
            Current user query:
            {query}

            Recent conversation context:
            {string.Join('\n', clippedMessages)}
            """;
    }

    private static string Clip(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
