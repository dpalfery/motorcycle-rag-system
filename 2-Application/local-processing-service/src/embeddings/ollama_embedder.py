"""Ollama embedding client for generating vector embeddings using local Ollama models."""

import asyncio
import logging
import os

import ollama

logger = logging.getLogger(__name__)

_MAX_RETRIES = 3


class OllamaEmbedder:
    """Generates embeddings via a local Ollama instance.

    Reads configuration from environment variables:
        OLLAMA_HOST            – Ollama server URL  (default: http://localhost:11434)
        OLLAMA_MODEL_EMBEDDING – Model name          (default: qwen3-embedding:4b)

    The embedder enforces 1536-dimensional output to match the Azure AI Search
    index (VectorSearchDimensions = 1536).  Qwen3-Embedding-4B natively produces
    3584 dims; MRL (Matryoshka Representation Learning) allows safe truncation
    to 1536 without quality loss.
    """

    def __init__(self) -> None:
        self._host: str = os.getenv("OLLAMA_HOST", "http://localhost:11434")
        self._model: str = os.getenv("OLLAMA_MODEL_EMBEDDING", "qwen3-embedding:4b")
        self._dims: int = 1536
        self._client: ollama.AsyncClient = ollama.AsyncClient(host=self._host)

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    async def generate_embedding(self, text: str) -> list[float]:
        """Return a 1536-dimensional embedding for *text*.

        Retries up to 3 times with exponential back-off on transient failures.
        Raises ``ValueError`` if the returned vector length != 1536 after
        truncation.
        """
        last_error: Exception | None = None

        for attempt in range(_MAX_RETRIES):
            try:
                response = await self._client.embed(
                    model=self._model,
                    input=text,
                )

                vector: list[float] = list(response.embeddings[0])

                # MRL truncation – safe for Matryoshka-trained models
                if len(vector) > self._dims:
                    vector = vector[: self._dims]

                if len(vector) != self._dims:
                    raise ValueError(f"Expected {self._dims} dims, got {len(vector)}")

                return vector

            except ValueError:
                # Dimension mismatch is a programming / config error – don't retry.
                raise
            except Exception as exc:
                last_error = exc
                logger.warning(
                    "Ollama embed attempt %d/%d failed: %s",
                    attempt + 1,
                    _MAX_RETRIES,
                    exc,
                )
                if attempt < _MAX_RETRIES - 1:
                    await asyncio.sleep(2**attempt)

        raise RuntimeError(
            f"Ollama embedding failed after {_MAX_RETRIES} retries"
        ) from last_error

    async def generate_embeddings_batch(self, texts: list[str]) -> list[list[float]]:
        """Return embeddings for a batch of texts.

        Processes texts concurrently with ``asyncio.gather`` for throughput.
        """
        tasks = [self.generate_embedding(text) for text in texts]
        return list(await asyncio.gather(*tasks))

    async def check_ollama_status(self) -> str:
        """Return ``'connected'`` if the Ollama server is reachable, else ``'disconnected'``.

        This method **never** raises – it is safe to call from health-check
        endpoints.
        """
        try:
            await self._client.list()
            return "connected"
        except Exception:
            return "disconnected"
