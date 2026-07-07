"""Azure AI Foundry Local embedding client for generating vector embeddings via OpenAI-compatible server."""

import asyncio
import logging
import os
import time

import httpx
import openai

logger = logging.getLogger(__name__)

_MAX_RETRIES = 3
_DEFAULT_REQUEST_TIMEOUT_SECONDS = 120.0
_DEFAULT_HEALTH_TIMEOUT_SECONDS = 10.0
_HEALTH_CACHE_TTL_SECONDS = 60.0


def _get_positive_float_env(name: str, default_value: float) -> float:
    raw_value = os.getenv(name, "").strip()
    if not raw_value:
        return default_value

    try:
        value = float(raw_value)
    except ValueError:
        logger.warning("Invalid %s value; using default.", name)
        return default_value

    if value <= 0:
        logger.warning("Non-positive %s value; using default.", name)
        return default_value

    return value


def _normalize_openai_base_url(endpoint: str) -> str:
    normalized = endpoint.strip().rstrip("/")
    if not normalized:
        return ""

    for suffix in ("/models", "/embeddings", "/chat/completions"):
        if normalized.lower().endswith(suffix):
            normalized = normalized[: -len(suffix)]
            break

    return normalized


class AzureFoundryLocalEmbedder:
    """Generates embeddings via Azure AI Foundry Local (OpenAI-compatible server).

    Reads from env:
        AZURE_FOUNDRY_LOCAL_ENDPOINT        – server URL (default: http://localhost:5272)
        AZURE_FOUNDRY_LOCAL_EMBEDDING_MODEL – model name (default: qwen3-embedding)
        AZURE_FOUNDRY_LOCAL_EMBEDDING_DIMS  – expected vector dimension (optional; skips check when unset)

    Azure AI Foundry Local accepts any non-empty string as the API key — we use
    ``"local"`` as a fixed placeholder.

    Enforces a configurable dimension check to match the Azure AI Search index
    (VectorSearchDimensions). Set AZURE_FOUNDRY_LOCAL_EMBEDDING_DIMS or leave
    unset to skip validation.
    """

    def __init__(self, endpoint: str | None = None, model: str | None = None) -> None:
        self._endpoint: str = _normalize_openai_base_url(
            endpoint or os.getenv("AZURE_FOUNDRY_LOCAL_ENDPOINT", "http://localhost:5272/v1")
        )
        self._model: str = model or os.getenv(
            "AZURE_FOUNDRY_LOCAL_EMBEDDING_MODEL", "qwen3-embedding"
        )
        dims_env = os.getenv("AZURE_FOUNDRY_LOCAL_EMBEDDING_DIMS")
        self._dims: int | None = int(dims_env) if dims_env else None
        self._request_timeout_seconds = _get_positive_float_env(
            "EMBEDDING_REQUEST_TIMEOUT_SECONDS",
            _DEFAULT_REQUEST_TIMEOUT_SECONDS,
        )
        self._health_timeout_seconds = _get_positive_float_env(
            "EMBEDDING_HEALTH_TIMEOUT_SECONDS",
            _DEFAULT_HEALTH_TIMEOUT_SECONDS,
        )
        self._client: openai.AsyncOpenAI = openai.AsyncOpenAI(
            base_url=self._endpoint,
            api_key="local",
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
                response = await asyncio.wait_for(
                    self._client.embeddings.create(
                        model=self._model,
                        input=text,
                    ),
                    timeout=self._request_timeout_seconds,
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
        """Return ``'connected'`` if the embedding provider is reachable.

        Uses a lightweight GET request to the provider root (not /v1/models).
        Results are cached for up to 60 seconds to avoid flooding the provider
        with health probes.
        """
        now = time.monotonic()
        if self._last_health_status is not None and (now - self._last_health_time) < _HEALTH_CACHE_TTL_SECONDS:
            return self._last_health_status

        try:
            async with httpx.AsyncClient(
                timeout=httpx.Timeout(self._health_timeout_seconds)
            ) as client:
                response = await client.get(self._endpoint)
                response.raise_for_status()
            self._last_health_status = "connected"
        except Exception:
            self._last_health_status = "disconnected"

        self._last_health_time = now
        return self._last_health_status
