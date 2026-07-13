using Azure.AI.Projects;
using Azure.Identity;

namespace MotorcycleRAG.AgentProvisioning.Azure;

/// <summary>
/// Creates the Azure SDK implementation required by the Agent Provisioning CLI.
/// This is the composition boundary for Foundry credentials and clients; application
/// behavior depends only on <see cref="IAgentAdminOperations"/>.
/// </summary>
internal static class AgentProvisioningComposition
{
    internal static IAgentAdminOperations CreateAdminOperations(Uri foundryEndpoint)
    {
        ArgumentNullException.ThrowIfNull(foundryEndpoint);

        var credential = new DefaultAzureCredential();
        var projectClient = new AIProjectClient(foundryEndpoint, credential);
        return new FoundryAgentAdminClientAdapter(projectClient.AgentAdministrationClient);
    }
}
