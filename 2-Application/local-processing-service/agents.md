# Agent Context: local-processing-service (2-Application / Python)

## Documentation

Before changing this cataloged component, read the [documentation standard](../../6-Docs/documentation-standard.md) and [component catalog](../../6-Docs/catalog.md), then update the canonical documentation when applicable.

This file is **local-processor specific** context. Root rules live in `AGENTS.md`.

## What to read first (authoritative)
- Baseline requirements + trust policy: `specs/001-system-spec/spec.md`
- Security checklist: `specs/001-system-spec/checklists/asvs-v5-level2.md`

## Inference Provider

**LM Studio is the default local inference provider (NOT Ollama).**

- `EMBEDDING_PROVIDER_ENDPOINT=http://localhost:1234` points to LM Studio's OpenAI-compatible server.
- `EMBEDDING_BACKEND=ollama` is overridden by `EMBEDDING_PROVIDER_ENDPOINT` in `embedder_factory.py`.
- Model discovery detects LM Studio via `/v1/models` and instantiates `OpenAIEmbedder`, not `OllamaEmbedder`.

## Dimension Handling

- Target embedding dimension: **1536**.
- LM Studio does **NOT** support the `dimensions` parameter for server-side truncation.
- Client-side **Matryoshka Representation Learning (MRL)** truncation is applied via `TruncatingEmbedder`.
- `TruncatingEmbedder` wraps the selected embedder, applies first-N-dimensions slicing on all outputs.

## What belongs here (in this repo)
- Python embedder implementations (`OpenAIEmbedder`, `OllamaEmbedder`, `TruncatingEmbedder`)
- Embedder factory and model discovery
- Graph processors and ingestion pipeline
- PDF/CSV chunking logic

## What must NOT be here
- No C#/.NET code (that's other layers)
- No Azure SDK usage (that's Persistence)
- No HTTP concerns for the BFF (that's Presentation)

## Useful commands
- Run: `python src/main.py` (from service root)
- Test: `pytest`
