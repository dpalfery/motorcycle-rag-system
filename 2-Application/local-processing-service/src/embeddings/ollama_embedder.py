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
        OLLAMA_BASE_URL – Ollama server URL  (default: http://localhost:11434)
        OLLAMA_MODEL    – Model name         (default: qwen3-embedding)

    Backwards-compatible fallbacks (older names):
        OLLAMA_HOST
        OLLAMA_MODEL_EMBEDDING

    The embedder enforces 3584-dimensional output to match the Azure AI Search
    index (VectorSearchDimensions = 3584).  Qwen3-Embedding-4B natively produces
    3584 dims; the full native dimensions are used without truncation.
    """

    def __init__(self, host: str | None = None, model: str | None = None) -> None:
        self._host: str = (host or os.getenv("OLLAMA_BASE_URL") or os.getenv(
            "OLLAMA_HOST", "http://localhost:11434"
        )).rstrip("/")
        self._model: str = model or os.getenv("OLLAMA_MODEL") or os.getenv(
            "OLLAMA_MODEL_EMBEDDING", "qwen3-embedding"
        )
        dims_env = os.getenv("OLLAMA_EMBEDDING_DIMS")
        self._dims: int | None = int(dims_env) if dims_env else None
        self._client: ollama.AsyncClient = ollama.AsyncClient(host=self._host)

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    async def generate_embedding(self, text: str) -> list[float]:
        """Return a 3584-dimensional embedding for *text*.

        Retries up to 3 times with exponential back-off on transient failures.
        Raises ``ValueError`` if the returned vector length != 3584.
        """
        last_error: Exception | None = None

        for attempt in range(_MAX_RETRIES):
            try:
                response = await self._client.embed(
                    model=self._model,
                    input=text,
                )

                vector: list[float] = list(response.embeddings[0])

                if self._dims is not None and len(vector) != self._dims:
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

    async def check_status(self) -> str:
        return await self.check_ollama_status()
