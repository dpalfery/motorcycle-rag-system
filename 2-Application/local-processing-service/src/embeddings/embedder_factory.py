"""Factory for selecting the embedding backend based on EMBEDDING_BACKEND env var."""

import logging
import os
from typing import Union

from .deepinfra_embedder import DeepInfraEmbedder
from .foundry_local_embedder import AzureFoundryLocalEmbedder
from .model_discovery import discover_embedding_models_sync
from .ollama_embedder import OllamaEmbedder

logger = logging.getLogger(__name__)

EmbedderType = Union[OllamaEmbedder, DeepInfraEmbedder, AzureFoundryLocalEmbedder]

_VALID_BACKENDS = {"ollama", "foundry_local", "deepinfra"}

_embedder_instance: EmbedderType | None = None


def _select_default_model(models: list[str]) -> str:
    if not models:
        raise ValueError("No models were discovered for the configured embedding provider endpoint")

    if len(models) > 1:
        raise ValueError(
            "Multiple models were discovered for the configured embedding provider endpoint. "
            "Set EMBEDDING_MODEL explicitly."
        )

    return models[0]


def get_embedder() -> EmbedderType:
    """Return the singleton embedder for the configured backend.

    EMBEDDING_BACKEND env var selects the backend:
        ollama        – local Ollama server (default)
        foundry_local – Azure AI Foundry Local server (port 5272)
        deepinfra     – DeepInfra API (requires DEEPINFRA_API_KEY)

    Raises:
        ValueError: If EMBEDDING_BACKEND has an unrecognised value.
    """
    global _embedder_instance
    if _embedder_instance is not None:
        return _embedder_instance

    provider_endpoint = os.getenv("EMBEDDING_PROVIDER_ENDPOINT", "").strip()
    selected_model = os.getenv("EMBEDDING_MODEL", "").strip() or None

    if provider_endpoint:
        discovery = discover_embedding_models_sync(provider_endpoint)
        logger.info(
            "Initialising embedding provider from endpoint %s using %s",
            provider_endpoint,
            discovery.provider,
        )

        if discovery.provider == "ollama":
            _embedder_instance = OllamaEmbedder(
                host=discovery.endpoint,
                model=selected_model or _select_default_model(discovery.models),
            )
        else:
            _embedder_instance = AzureFoundryLocalEmbedder(
                endpoint=discovery.endpoint,
                model=selected_model or _select_default_model(discovery.models),
            )

        return _embedder_instance

    backend = os.getenv("EMBEDDING_BACKEND", "ollama").lower().strip()
    logger.info("Initialising embedding backend: %s", backend)

    if backend == "ollama":
        _embedder_instance = OllamaEmbedder()
    elif backend == "foundry_local":
        _embedder_instance = AzureFoundryLocalEmbedder()
    elif backend == "deepinfra":
        _embedder_instance = DeepInfraEmbedder()
    else:
        raise ValueError(
            f"Unknown EMBEDDING_BACKEND '{backend}'. "
            f"Valid options: {sorted(_VALID_BACKENDS)}"
        )

    return _embedder_instance


def reset_embedder() -> None:
    """Reset singleton for testing — do NOT call in production code."""
    global _embedder_instance
    _embedder_instance = None
