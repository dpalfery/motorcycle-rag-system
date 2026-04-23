"""DeepInfra embedding client for generating vector embeddings via the OpenAI-compatible API."""

import asyncio
import logging
import os

import openai

logger = logging.getLogger(__name__)

_MAX_RETRIES = 3


class DeepInfraEmbedder:
    """Generates 3584-dim embeddings via DeepInfra OpenAI-compatible API.

    Reads configuration from environment variables:
        DEEPINFRA_API_KEY         – Bearer token (REQUIRED — raises ValueError if missing)
        DEEPINFRA_BASE_URL        – API base URL (default: https://api.deepinfra.com/v1/openai)
        DEEPINFRA_EMBEDDING_MODEL – model name   (default: Qwen/Qwen3-Embedding-4B)

    The embedder enforces 3584-dimensional output to match the Azure AI Search
    index (VectorSearchDimensions = 3584).  Qwen3-Embedding-4B natively produces
    3584 dims; the full native dimensions are used without truncation.
    """

    def __init__(self) -> None:
        api_key: str = os.getenv("DEEPINFRA_API_KEY", "")
        if not api_key:
            raise ValueError("DEEPINFRA_API_KEY environment variable is required")

        self._base_url: str = os.getenv(
            "DEEPINFRA_BASE_URL", "https://api.deepinfra.com/v1/openai"
        )
        self._model: str = os.getenv(
            "DEEPINFRA_EMBEDDING_MODEL", "Qwen/Qwen3-Embedding-4B"
        )
        self._dims: int = 3584
        self._client: openai.AsyncOpenAI = openai.AsyncOpenAI(
            base_url=self._base_url,
            api_key=api_key,
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
                    "DeepInfra embed attempt %d/%d failed: %s",
                    attempt + 1,
                    _MAX_RETRIES,
                    exc,
                )
                if attempt < _MAX_RETRIES - 1:
                    await asyncio.sleep(2**attempt)

        raise RuntimeError(
            f"DeepInfra embedding failed after {_MAX_RETRIES} retries"
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
