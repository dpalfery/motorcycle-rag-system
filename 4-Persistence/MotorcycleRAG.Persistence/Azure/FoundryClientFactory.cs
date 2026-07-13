using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Core;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Default implementation of <see cref="IFoundryClientFactory"/>. Constructs Azure Foundry
/// SDK clients. Registered as a singleton in DI
/// (see <c>ServiceCollectionExtensions.AddAzureServices</c>).
/// </summary>
public class FoundryClientFactory : IFoundryClientFactory
{
    /// <inheritdoc />
    public AIProjectClient CreateProjectClient(string endpoint, TokenCredential credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentNullException.ThrowIfNull(credential);
        return new AIProjectClient(new Uri(endpoint), credential);
    }

    /// <inheritdoc />
    public ProjectOpenAIClient CreateOpenAIClient(AIProjectClient projectClient)
    {
        ArgumentNullException.ThrowIfNull(projectClient);
        return projectClient.ProjectOpenAIClient;
    }

    /// <inheritdoc />
    public ProjectConversationsClient CreateConversationsClient(AIProjectClient projectClient)
    {
        ArgumentNullException.ThrowIfNull(projectClient);
        return projectClient.ProjectOpenAIClient.GetProjectConversationsClient();
    }
}
