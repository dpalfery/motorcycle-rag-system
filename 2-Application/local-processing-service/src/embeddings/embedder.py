"""Abstract base class for embedding providers."""

from __future__ import annotations

from abc import ABC, abstractmethod


class Embedder(ABC):
    """Interface for generating vector embeddings.

    All concrete embedder implementations inherit from this class so
    callers depend only on the abstraction, not on a specific provider.
    """

    @abstractmethod
    async def generate_embedding(self, text: str) -> list[float]:
        """Return a vector embedding for *text*."""

    @abstractmethod
    async def generate_embeddings_batch(self, texts: list[str]) -> list[list[float]]:
        """Return embeddings for a batch of texts."""

    @abstractmethod
    async def check_status(self) -> str:
        """Return a health status string (e.g. ``'connected'`` or ``'disconnected'``)."""
