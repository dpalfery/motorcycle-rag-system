"""Integration test: DeepInfraEmbedder → AzureSearchDirectUploader pipeline.

Verifies the full embedding-to-upload flow using mocks for all external
services (no real Azure or DeepInfra calls).
"""

import importlib
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

import pytest


@pytest.mark.slow
async def test_deepinfra_embedder_to_uploader_pipeline(monkeypatch):
    """Full pipeline: embed text via DeepInfra mock → upload doc to Azure Search mock."""

    # ── 1. Set env vars BEFORE constructing either component ──────────
    monkeypatch.setenv("DEEPINFRA_API_KEY", "test-integration-key")
    monkeypatch.setenv("DEEPINFRA_BASE_URL", "https://api.deepinfra.com/v1/openai")
    monkeypatch.setenv("AZURE_SEARCH_ENDPOINT", "https://test.search.windows.net")
    monkeypatch.setenv("AZURE_SEARCH_INDEX", "motorcycle-index")
    monkeypatch.setenv("AZURE_SEARCH_KEY", "test-admin-key")

    # ── 2. Mock the DeepInfra / OpenAI embeddings client ──────────────
    mock_embedding_obj = MagicMock()
    mock_embedding_obj.embedding = [0.1] * 3584

    mock_embed_response = MagicMock()
    mock_embed_response.data = [mock_embedding_obj]

    mock_openai_client = MagicMock()
    mock_openai_client.embeddings.create = AsyncMock(return_value=mock_embed_response)

    # ── 3. Mock the Azure Search client ───────────────────────────────
    mock_search_client = MagicMock()
    mock_search_client.merge_or_upload_documents.return_value = [
        SimpleNamespace(succeeded=True)
    ]

    # ── 4. Construct DeepInfraEmbedder (reload required — client set in __init__) ──
    with patch(
        "embeddings.deepinfra_embedder.openai.AsyncOpenAI",
        return_value=mock_openai_client,
    ):
        import embeddings.deepinfra_embedder as embedder_mod

        importlib.reload(embedder_mod)
        embedder = embedder_mod.DeepInfraEmbedder()

    # ── 5. Generate embedding ─────────────────────────────────────────
    vector = await embedder.generate_embedding("test motorcycle query")

    assert isinstance(vector, list)
    assert len(vector) == 3584

    # ── 6. Construct AzureSearchDirectUploader ────────────────────────
    with (
        patch(
            "search.azure_search_uploader.SearchClient",
            return_value=mock_search_client,
        ),
        patch("search.azure_search_uploader.AzureKeyCredential"),
    ):
        from search.azure_search_uploader import AzureSearchDirectUploader

        uploader = AzureSearchDirectUploader()

    # ── 7. Build document and upload ──────────────────────────────────
    doc = {
        "id": "test-integration-01",
        "contentVector": vector,
        "title": "integration test",
    }
    uploader.upload([doc])

    # ── 8. Assert upload called exactly once with correct payload ─────
    mock_search_client.merge_or_upload_documents.assert_called_once_with(
        documents=[doc]
    )
