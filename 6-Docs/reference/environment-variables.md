---
id: reference/environment-variables
title: Environment Variables Reference
doc-type: reference
status: current
owner: Platform maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# Environment Variables Reference

Environment variables are not a general configuration mechanism for this repository.

C#/.NET applications must use Azure App Configuration for application settings and Azure Key Vault references for secrets. Do not add `MCR_API_*`, `MCR_BFF_*`, `MCR_ADMIN_*`, or `MCR_MOBILE_*` environment-variable paths for .NET code. If a .NET setting is needed, add an `IConfiguration` option and populate it from App Configuration/Key Vault.

The only approved environment-variable surface is the Python local processor. Those values are set by the desktop admin client / admin desktop project at run time when it launches or manages the local processor.

---

## Python Local Processor

**Application**: `2-Application/local-processing-service`

These variables are read directly by the Python process. Do not use them from C#/.NET code.

### Logging

| Variable | Default | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `LOCAL_PROCESSOR_LOG_DIR` | `./logs` | Directory where daily rolling log files are written. Rotates at midnight, retains 14 days. | No | Non-Secret |

### Embedding Backend

| Variable | Default | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `EMBEDDING_BACKEND` | `ollama` | Selects which embedder to use at ingestion time. Valid: `ollama` or `openai`. | No | Non-Secret |

### Ollama

| Variable | Default | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `OLLAMA_BASE_URL` | `http://localhost:11434` | Base URL of the local Ollama server. | No | Non-Secret |
| `OLLAMA_MODEL` | `qwen3-embedding` | Ollama model name for embeddings. | No | Non-Secret |

### OpenAI-Compatible Embedder

| Variable | Default | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `EMBEDDING_PROVIDER_ENDPOINT` | `http://localhost:1234/v1` | Base URL of any OpenAI-compatible embedding server (LM Studio, Foundry Local, etc.). **LM Studio is the default local provider.** | No | Non-Secret |
| `EMBEDDING_MODEL` | `qwen3-embedding` | Model name for embeddings, used by the OpenAI-compatible embedder and for tokenizer resolution. | No | Non-Secret |
| `EMBEDDING_PROVIDER_API_KEY` | `local` | API key for cloud OpenAI-compatible providers. Leave unset for local servers. | No | Secret |

### Azure AI Search Upload

| Variable | Example | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `AZURE_SEARCH_ENDPOINT` | `https://<service>.search.windows.net` | Azure AI Search service endpoint. | Yes, for direct indexing | Non-Secret |
| `AZURE_SEARCH_INDEX` | `motorcycle-index` | Target search index. | No | Non-Secret |
| `AZURE_SEARCH_KEY` | `xxxxxxxxxxxxx` | Azure AI Search API key. Leave blank to use identity-based auth where supported. | No | Secret |

### MotorcycleRAG API Upload

The Python service uses client credentials to POST processed artifacts to the API. The desktop admin client / admin desktop project is responsible for supplying these process environment values at run time.

| Variable | Default | Purpose | Required | Type |
| --- | --- | --- | --- | --- |
| `MCR_API_BASE_URL` | `https://localhost:5001` | Base URL of the MotorcycleRAG API. | Yes | Non-Secret |
| `MCR_LOCAL_PROCESSOR_CLIENT_ID` | `d09d356d-62ac-4f38-b636-64169119ea25` | Entra client ID of the Python-Upload-Job app registration. | No | Non-Secret |
| `PYTHON_UPLOAD_JOB_SECRET` | - | Client secret for the Python-Upload-Job app registration. If absent, artifact upload is disabled. | Yes, for upload | Secret |
| `MCR_LOCAL_PROCESSOR_TENANT_ID` | `0f8f8a52-f135-43af-af88-e0b54ca9ff91` | Entra tenant ID. | No | Non-Secret |
| `MCR_API_SCOPE` | `api://motorcyclerag-api/.default` | OAuth scope for M2M token requests. Always use `/.default` for client credentials. | No | Non-Secret |
