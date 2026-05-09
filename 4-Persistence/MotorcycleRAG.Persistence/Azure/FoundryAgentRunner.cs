using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using OpenAI.Responses;
using System.Collections.Concurrent;
using System.ClientModel;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Implements <see cref="IFoundryAgentRunner"/> using the new Microsoft Foundry
/// conversations and responses API.
/// </summary>
public sealed class FoundryAgentRunner : IFoundryAgentRunner
{
    private readonly ProjectConversationsClient _conversationsClient;
    private readonly ProjectOpenAIClient _openAIClient;
    private readonly IReadOnlyDictionary<string, string> _agentVersions;
    private readonly ConcurrentDictionary<string, List<ResponseItem>> _conversationItems = new();
    private readonly ILogger<FoundryAgentRunner> _logger;

    public FoundryAgentRunner(
        IOptions<AzureFoundryOptions> options,
        ILogger<FoundryAgentRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var config = options.Value ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(config.FoundryEndpoint))
            throw new InvalidOperationException("AzureAI:FoundryEndpoint is required for FoundryAgentRunner");

        var projectClient = new AIProjectClient(new Uri(config.FoundryEndpoint), new DefaultAzureCredential());
        _openAIClient = projectClient.ProjectOpenAIClient;
        _conversationsClient = _openAIClient.GetProjectConversationsClient();
        _agentVersions = BuildAgentVersionMap(config);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> CreateConversationAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Creating Foundry conversation");
        var conversation = await _conversationsClient.CreateProjectConversationAsync(
            new ProjectConversationCreationOptions(),
            ct);

        _conversationItems[conversation.Value.Id] = [];
        _logger.LogDebug("Created Foundry conversation {ConversationId}", conversation.Value.Id);
        return conversation.Value.Id;
    }

    /// <inheritdoc />
    public Task<AgentResponseStatus> SendAgentMessageAsync(
        string conversationId,
        string agentName,
        string content,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        return CreateAgentResponseAsync(
            conversationId,
            agentName,
            [ResponseItem.CreateUserMessageItem(content)],
            ct);
    }

    /// <inheritdoc />
    public Task<AgentResponseStatus> SubmitToolOutputsAsync(
        string conversationId,
        string agentName,
        IEnumerable<AgentToolOutput> outputs,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(outputs);

        var outputItems = outputs
            .Select(o => ResponseItem.CreateFunctionCallOutputItem(o.CallId, o.Output))
            .Cast<ResponseItem>()
            .ToArray();

        _logger.LogDebug(
            "Submitting {Count} tool outputs for conversation {ConversationId} to agent {AgentName}",
            outputItems.Length,
            conversationId,
            agentName);

        return CreateAgentResponseAsync(conversationId, agentName, outputItems, ct);
    }

    /// <inheritdoc />
    public async Task DeleteConversationAsync(string conversationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);

        _conversationItems.TryRemove(conversationId, out _);

        try
        {
            await _conversationsClient.DeleteConversationAsync(conversationId, options: null);
            _logger.LogDebug("Deleted Foundry conversation {ConversationId}", conversationId);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Foundry conversation {ConversationId} was already deleted", conversationId);
        }
    }

    private async Task<AgentResponseStatus> CreateAgentResponseAsync(
        string conversationId,
        string agentName,
        IReadOnlyList<ResponseItem> newItems,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        var inputItems = _conversationItems.GetOrAdd(conversationId, _ => []);
        lock (inputItems)
        {
            inputItems.AddRange(newItems);
        }

        var requestItems = SnapshotItems(inputItems);
        _agentVersions.TryGetValue(agentName, out var configuredVersion);
        var responsesClient = _openAIClient.GetProjectResponsesClientForAgent(
            new AgentReference(agentName, string.IsNullOrWhiteSpace(configuredVersion) ? null : configuredVersion),
            null!);

        _logger.LogDebug(
            "Creating Foundry response for conversation {ConversationId} with agent {AgentName} and {ItemCount} input items",
            conversationId,
            agentName,
            requestItems.Count);

        var response = await responsesClient.CreateResponseAsync(requestItems, conversationId, ct);
        var status = MapToAgentResponseStatus(response.Value);

        lock (inputItems)
        {
            inputItems.AddRange(response.Value.OutputItems);
        }

        _logger.LogDebug(
            "Foundry response {ResponseId} for conversation {ConversationId}: state={State}, toolCalls={ToolCallCount}",
            status.ResponseId,
            conversationId,
            status.State,
            status.RequiredToolCalls?.Count ?? 0);

        return status;
    }

    private static IReadOnlyList<ResponseItem> SnapshotItems(List<ResponseItem> items)
    {
        lock (items)
        {
            return items.ToArray();
        }
    }

    private static AgentResponseStatus MapToAgentResponseStatus(ResponseResult response)
    {
        var toolCalls = response.OutputItems
            .OfType<FunctionCallResponseItem>()
            .Select(tc => new AgentToolCall(
                tc.CallId,
                tc.FunctionName,
                tc.FunctionArguments.ToString()))
            .ToList()
            .AsReadOnly();

        if (toolCalls.Count > 0)
            return new AgentResponseStatus(response.Id, AgentRunState.RequiresAction, toolCalls, response.GetOutputText());

        var state = response.Status == ResponseStatus.Completed
            ? AgentRunState.Completed
            : AgentRunState.Failed;

        return new AgentResponseStatus(response.Id, state, null, response.GetOutputText());
    }

    private static IReadOnlyDictionary<string, string> BuildAgentVersionMap(AzureFoundryOptions config)
    {
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        AddVersion(config.OrchestratorAgentName, config.OrchestratorAgentVersion, versions);
        AddVersion(config.VectorSearchAgentName, config.VectorSearchAgentVersion, versions);
        AddVersion(config.WebSearchAgentName, config.WebSearchAgentVersion, versions);
        AddVersion(config.PDFSearchAgentName, config.PDFSearchAgentVersion, versions);
        AddVersion(config.GraphQueryAgentName, config.GraphQueryAgentVersion, versions);
        return versions;
    }

    private static void AddVersion(string name, string version, IDictionary<string, string> versions)
    {
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(version))
            versions[name] = version;
    }
}
