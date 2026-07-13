using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Core;

namespace MotorcycleRAG.Persistence.Azure;

/// <summary>
/// Factory abstraction for creating Azure Foundry SDK clients.
/// </summary>
/// <remarks>
/// <para>
/// This interface lives in <b>Persistence</b> (not Contracts) because it returns the
/// concrete <see cref="AIProjectClient"/>, <see cref="ProjectOpenAIClient"/>, and
/// <see cref="ProjectConversationsClient"/> Azure SDK types. Contracts is forbidden from
/// referencing any infrastructure SDK, so the abstraction must live alongside its sole
/// consumer (<see cref="FoundryAgentRunner"/>) in the Persistence layer.
/// </para>
/// <para>
/// Extracted from the sealed SDK client construction previously inlined in the
/// <see cref="FoundryAgentRunner"/> constructor so that the runner can be unit tested
/// without instantiating real Azure SDK clients.
/// </para>
/// </remarks>
public interface IFoundryClientFactory
{
    /// <summary>
    /// Creates an <see cref="AIProjectClient"/> bound to the supplied Foundry endpoint
    /// (URI string) and authenticated with the given credential.
    /// </summary>
    AIProjectClient CreateProjectClient(string endpoint, TokenCredential credential);

    /// <summary>
    /// Resolves the <see cref="ProjectOpenAIClient"/> from an existing
    /// <see cref="AIProjectClient"/>.
    /// </summary>
    ProjectOpenAIClient CreateOpenAIClient(AIProjectClient projectClient);

    /// <summary>
    /// Resolves the <see cref="ProjectConversationsClient"/> from an existing
    /// <see cref="AIProjectClient"/>.
    /// </summary>
    ProjectConversationsClient CreateConversationsClient(AIProjectClient projectClient);
}
