# Problems — hybrid-embedding-pipeline

## No blockers at plan creation time.

Notes:
- T09 and T10 both modify `AzureOpenAIClientWrapper.cs` — must be sequential delegations
- Azure AI Foundry Local specifics (exact model name for qwen3-embedding, port config) may need validation when T04 is implemented — agent should check Microsoft docs at implementation time
- `IHttpClientFactory` injection for .NET (T09) requires verifying DI registration in startup — agent must check before modifying constructor
