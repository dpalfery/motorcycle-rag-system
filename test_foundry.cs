using System;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.AI.Projects;
using Azure.AI.Extensions.OpenAI;
using OpenAI.Responses;
using System.ClientModel;

class Program
{
    static async Task Main(string[] args)
    {
        try {
            Console.WriteLine(""Connecting to AIProjectClient..."");
            var client = new AIProjectClient(new Uri(""https://mcr-rag-dev-foundry-eastus-49bcb82b.services.ai.azure.com/api/projects/motorcycle-rag""), new DefaultAzureCredential());
            var openAIClient = client.ProjectOpenAIClient;
            
            var responsesClient = openAIClient.GetProjectResponsesClientForAgent(new AgentReference(""MCR-OrchestratorAgent"", ""4""), null!);
            
            Console.WriteLine(""Creating response..."");
            var requestItems = new ResponseItem[] { ResponseItem.CreateUserMessageItem(""Hello"") };
            var response = await responsesClient.CreateResponseAsync(requestItems, previousResponseId: null, cancellationToken: default);
            
            Console.WriteLine(""Success!"");
            Console.WriteLine(response.Value.GetOutputText());
        } catch (Exception ex) {
            Console.WriteLine(""Error: "" + ex.Message);
        }
    }
}
