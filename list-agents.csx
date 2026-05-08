using Azure.AI.Agents.Persistent;
using Azure.Identity;

var foundryEndpoint = "https://mcr-rag-dev-foundry-eus2-49bcb82b.services.ai.azure.com/api/projects/motorcycle-rag";
Console.WriteLine($"Connecting to Foundry endpoint: {foundryEndpoint}");

var client = new PersistentAgentsClient(foundryEndpoint, new DefaultAzureCredential());

try 
{
    Console.WriteLine("Listing agents...");
    await foreach (var agent in client.GetAgentsAsync())
    {
        Console.WriteLine($"Agent ID: {agent.Id}");
        Console.WriteLine($"  Name: {agent.Name}");
        Console.WriteLine($"  Model: {agent.Model}");
        Console.WriteLine($"  Created: {agent.CreatedAt}");
        Console.WriteLine();
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Inner: {ex.InnerException.Message}");
    }
}
