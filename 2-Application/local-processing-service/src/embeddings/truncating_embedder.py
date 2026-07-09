"""Decorator that truncates embedding vectors client-side.

Some inference providers — notably **LM Studio** — do not support the
``dimensions`` request parameter used for server-side Matryoshka (MRL)
truncation.  Models trained with Matryoshka Representation Learning
(e.g. Qwen3-Embedding-4B, natively 2560 dims) therefore return
full-length vectors regardless of the requested dimensionality.

:class:`TruncatingEmbedder` wraps any :class:`Embedder` and slices each
returned vector to its first ``target_dims`` elements before handing it
to the caller.  Matryoshka-trained vectors are designed so the leading
prefix is itself a high-quality embedding, which makes simple prefix
slicing equivalent to server-side truncation.

This follows the **Decorator Pattern**: the wrapped embedder keeps full
responsibility for transport / retry logic, while truncation logic is
centralised in a single place.
"""

from __future__ import annotations

import logging

from .embedder import Embedder

logger = logging.getLogger(__name__)


class TruncatingEmbedder(Embedder):
    """Wraps an :class:`Embedder` and truncates every vector to ``target_dims``.

    Args:
        embedder: The wrapped embedder instance (e.g. ``OllamaEmbedder``
            or ``OpenAIEmbedder``).
        target_dims: Number of leading dimensions to retain. Defaults to
            ``1536`` to match the Azure AI Search index configuration
            (``VectorSearchDimensions = 1536``).

    Truncation uses a prefix slice ``vector[:target_dims]``, which is
    valid for Matryoshka-trained models whose leading elements form a
    coherent sub-embedding.  Vectors that are already no longer than
    ``target_dims`` are returned unchanged to avoid needless copies on
    the hot path.
    """

    def __init__(self, embedder: Embedder, target_dims: int = 1536) -> None:
        self._embedder: Embedder = embedder
        self._target_dims: int = target_dims

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    async def generate_embedding(self, text: str) -> list[float]:
        """Return a truncated embedding for *text*.

        Delegates to the wrapped embedder, then slices the resulting
        vector to ``target_dims``.
        """
        vector = await self._embedder.generate_embedding(text)
        return self._truncate(vector)

    async def generate_embeddings_batch(self, texts: list[str]) -> list[list[float]]:
        """Return truncated embeddings for a batch of texts.

        Delegates to the wrapped embedder's batch method, then slices
        each vector individually.
        """
        vectors = await self._embedder.generate_embeddings_batch(texts)
        return [self._truncate(vector) for vector in vectors]

    async def check_status(self) -> str:
        """Return the health status of the wrapped embedder."""
        return await self._embedder.check_status()

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    def _truncate(self, vector: list[float]) -> list[float]:
        """Slice *vector* to ``target_dims`` (no-op if already short enough)."""
        if len(vector) <= self._target_dims:
            return vector

        logger.debug(
            "Truncating embedding from %d to %d dims (client-side MRL slice)",
            len(vector),
            self._target_dims,
        )
        return vector[: self._target_dims]
