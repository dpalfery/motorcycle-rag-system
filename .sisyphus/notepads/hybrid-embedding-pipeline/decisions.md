# Decisions — hybrid-embedding-pipeline

## 2026-03-01 — Use full 3584 native dims (no MRL truncation)
User explicitly chose full 3584 dims over 1536 MRL-truncated dims.
Rationale: no real indexed data exists yet (all mock), so schema migration cost is zero. Full dims = maximum retrieval quality.
Impact: `VectorSearchDimensions` in `MotorcycleIndexingService.cs` must change 1536 → 3584. `OllamaEmbedder` MRL truncation must be removed. All new embedders emit 3584 dims.

## 2026-03-01 — Keep blob→indexer pull model alongside new direct-push model
The new `AzureSearchDirectUploader` (direct push) runs in parallel with the existing JSONL blob write, not as a replacement.
Graceful degradation: if `AZURE_SEARCH_ENDPOINT` is not set, skip direct push, log warning, blob write continues as before.

## 2026-03-01 — DeepInfra for query-time .NET embeddings
`AzureOpenAIClientWrapper.GetEmbeddingsAsync` will call DeepInfra HTTP API directly.
Auth: `Authorization: Bearer {DEEPINFRA_API_KEY}` (env var, never hardcoded).
No new NuGet packages — use `IHttpClientFactory` (already in .NET ecosystem).

## 2026-03-01 — Azure AI Foundry serverless for .NET chat
`AzureOpenAIClientWrapper.GetChatCompletionAsync` will call Foundry serverless.
Auth: `DefaultAzureCredential` bearer token, scope `https://cognitiveservices.azure.com/.default`.
Env vars: `MCR_API_FOUNDRY_ENDPOINT`, `MCR_API_FOUNDRY_CHAT_MODEL` (follows MCR_API_* naming convention per environment-variables.md).

## 2026-03-01 — EmbedderFactory singleton pattern
Module-level singleton in `embedder_factory.py`. `EMBEDDING_BACKEND` env var selects: `ollama` (default) / `foundry_local` / `deepinfra`.
