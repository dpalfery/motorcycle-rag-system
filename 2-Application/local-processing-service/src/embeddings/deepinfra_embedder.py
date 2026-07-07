"""DeepInfra embedding client for generating vector embeddings via the OpenAI-compatible API."""

import asyncio
import logging
import os
import time

import openai

logger = logging.getLogger(__name__)

_MAX_RETRIES = 3
_HEALTH_CACHE_TTL_SECONDS = 60.0


class DeepInfraEmbedder:
    """Generates embeddings via DeepInfra OpenAI-compatible API.

    Reads configuration from environment variables:
        DEEPINFRA_API_KEY         – Bearer token (REQUIRED — raises ValueError if missing)
        DEEPINFRA_BASE_URL        – API base URL (default: https://api.deepinfra.com/v1/openai)
        DEEPINFRA_EMBEDDING_MODEL – model name   (default: Qwen/Qwen3-Embedding-4B)
        DEEPINFRA_EMBEDDING_DIMS  – expected vector dimension (optional; skips check when unset)

    Enforces a configurable dimension check to match the Azure AI Search index
    (VectorSearchDimensions). Set DEEPINFRA_EMBEDDING_DIMS or leave unset to
    skip validation.
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
        dims_env = os.getenv("DEEPINFRA_EMBEDDING_DIMS")
        self._dims: int | None = int(dims_env) if dims_env else None
        self._client: openai.AsyncOpenAI = openai.AsyncOpenAI(
            base_url=self._base_url,
            api_key=api_key,
        )
        self._last_health_status: str | None = None
        self._last_health_time: float = 0.0

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

                if self._dims is not None and len(vector) != self._dims:
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
        """Return ``'connected'`` if the DeepInfra API is reachable.

        Results are cached for up to 60 seconds to avoid flooding the API
        with health probes.
        """
        now = time.monotonic()
        if self._last_health_status is not None and (now - self._last_health_time) < _HEALTH_CACHE_TTL_SECONDS:
            return self._last_health_status

        try:
            await self._client.models.list()
            self._last_health_status = "connected"
        except Exception:
            self._last_health_status = "disconnected"

        self._last_health_time = now
        return self._last_health_status
