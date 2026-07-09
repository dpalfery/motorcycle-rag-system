"""Factory for selecting the embedding backend based on EMBEDDING_BACKEND env var."""

from __future__ import annotations

import logging
import os

from .embedder import Embedder
from .model_discovery import discover_embedding_models_sync
from .ollama_embedder import OllamaEmbedder
from .openai_embedder import OpenAIEmbedder
from .truncating_embedder import TruncatingEmbedder

logger = logging.getLogger(__name__)

_VALID_BACKENDS = {"ollama", "openai"}

_embedder_instance: Embedder | None = None


def _select_default_model(models: list[str]) -> str:
    if not models:
        raise ValueError("No models were discovered for the configured embedding provider endpoint")

    if len(models) > 1:
        raise ValueError(
            "Multiple models were discovered for the configured embedding provider endpoint. "
            "Set EMBEDDING_MODEL explicitly."
        )

    return models[0]


def _resolve_target_dims(is_ollama: bool) -> int:
    """Resolve the target dimensionality for client-side truncation.

    Reads ``OLLAMA_EMBEDDING_DIMS`` for the Ollama backend and
    ``EMBEDDING_DIMS`` otherwise, defaulting to 1536 to match the Azure
    AI Search index configuration.
    """
    env_name = "OLLAMA_EMBEDDING_DIMS" if is_ollama else "EMBEDDING_DIMS"
    return int(os.getenv(env_name, "1536"))


def get_embedder() -> Embedder:
    """Return the singleton embedder for the configured backend.

    EMBEDDING_BACKEND env var selects the backend:
        ollama  – local Ollama server (default)
        openai  – any OpenAI-compatible server (LM Studio, Foundry Local, etc.)

    The created embedder is wrapped in a :class:`TruncatingEmbedder` so
    that vectors are sliced to the configured dimensionality client-side.
    This is required for providers such as LM Studio that ignore the
    ``dimensions`` request parameter and return full-length vectors
    (e.g. 2560 dims from Qwen3-Embedding-4B).

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

        is_ollama = discovery.provider == "ollama"
        if is_ollama:
            base_embedder: Embedder = OllamaEmbedder(
                host=discovery.endpoint,
                model=selected_model or _select_default_model(discovery.models),
            )
        else:
            base_embedder = OpenAIEmbedder(
                endpoint=discovery.endpoint,
                model=selected_model or _select_default_model(discovery.models),
            )
    else:
        backend = os.getenv("EMBEDDING_BACKEND", "ollama").lower().strip()
        logger.info("Initialising embedding backend: %s", backend)

        if backend == "ollama":
            is_ollama = True
            base_embedder = OllamaEmbedder()
        elif backend == "openai":
            is_ollama = False
            base_embedder = OpenAIEmbedder()
        else:
            raise ValueError(
                f"Unknown EMBEDDING_BACKEND '{backend}'. "
                f"Valid options: {sorted(_VALID_BACKENDS)}"
            )

    target_dims = _resolve_target_dims(is_ollama)
    logger.info(
        "Wrapping embedder with TruncatingEmbedder (target_dims=%d)", target_dims
    )
    _embedder_instance = TruncatingEmbedder(base_embedder, target_dims=target_dims)
    return _embedder_instance


def reset_embedder() -> None:
    """Reset singleton for testing — do NOT call in production code."""
    global _embedder_instance
    _embedder_instance = None
