"""Azure AI Foundry Local embedding client for generating vector embeddings via OpenAI-compatible server."""

import asyncio
import logging
import os

import openai

logger = logging.getLogger(__name__)

_MAX_RETRIES = 3


class AzureFoundryLocalEmbedder:
    """Generates 3584-dim embeddings via Azure AI Foundry Local (OpenAI-compatible server).

    Reads from env:
        AZURE_FOUNDRY_LOCAL_ENDPOINT        – server URL (default: http://localhost:5272)
        AZURE_FOUNDRY_LOCAL_EMBEDDING_MODEL – model name (default: qwen3-embedding)

    Azure AI Foundry Local accepts any non-empty string as the API key — we use
    ``"local"`` as a fixed placeholder.

    The embedder enforces 3584-dimensional output to match the Azure AI Search
    index (VectorSearchDimensions = 3584).  Qwen3-Embedding natively produces
    3584 dims; the full native dimensions are used without truncation.
    """

    def __init__(self, endpoint: str | None = None, model: str | None = None) -> None:
        self._endpoint: str = (endpoint or os.getenv(
            "AZURE_FOUNDRY_LOCAL_ENDPOINT", "http://localhost:5272"
        )).rstrip("/")
        self._model: str = model or os.getenv(
            "AZURE_FOUNDRY_LOCAL_EMBEDDING_MODEL", "qwen3-embedding"
        )
        self._dims: int = 3584
        self._client: openai.AsyncOpenAI = openai.AsyncOpenAI(
            base_url=self._endpoint,
            api_key="local",
        )

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
                response = await self._client.embeddings.create(
                    model=self._model,
                    input=text,
                )

                vector: list[float] = response.data[0].embedding

                if len(vector) != self._dims:
                    raise ValueError(f"Expected {self._dims} dims, got {len(vector)}")

                return vector

            except ValueError:
                # Dimension mismatch is a programming / config error – don't retry.
                raise
            except Exception as exc:
                last_error = exc
                logger.warning(
                    "Foundry Local embed attempt %d/%d failed: %s",
                    attempt + 1,
                    _MAX_RETRIES,
                    exc,
                )
                if attempt < _MAX_RETRIES - 1:
                    await asyncio.sleep(2**attempt)

        raise RuntimeError(
            f"Foundry Local embedding failed after {_MAX_RETRIES} retries"
        ) from last_error

    async def generate_embeddings_batch(self, texts: list[str]) -> list[list[float]]:
        """Return embeddings for a batch of texts.

        Processes texts concurrently with ``asyncio.gather`` for throughput.
        """
        tasks = [self.generate_embedding(text) for text in texts]
        return list(await asyncio.gather(*tasks))

    async def check_status(self) -> str:
        try:
            await self._client.models.list()
            return "connected"
        except Exception:
            return "disconnected"
