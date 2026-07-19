"""Ollama embedding client for generating vector embeddings using local Ollama models."""

import asyncio
import logging
import os
import time

import ollama

from security.safe_http import validate_model_provider_endpoint

from .embedder import Embedder

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


class OllamaEmbedder(Embedder):
    """Generates embeddings via a local Ollama instance.

    Reads configuration from environment variables:
        OLLAMA_BASE_URL – Ollama server URL  (default: http://localhost:11434)
        OLLAMA_MODEL    – Model name         (default: qwen3-embedding)

    Backwards-compatible fallbacks (older names):
        OLLAMA_HOST
        OLLAMA_MODEL_EMBEDDING

    The host (constructor argument, ``OLLAMA_BASE_URL``, or ``OLLAMA_HOST``)
    is validated at construct time with the model-provider endpoint policy
    (public HTTPS or literal-loopback HTTP). Embed/list/health traffic still
    uses ``ollama.AsyncClient`` directly (no mid-flight DNS pin).

    The embedder enforces 1536-dimensional output to match the Azure AI Search
    index (VectorSearchDimensions = 1536) via server-side Matryoshka truncation.
    Qwen3-Embedding-4B is natively 2560 dims; the ``dimensions=1536`` parameter
    is passed to the API call to produce exactly 1536-dim vectors.
    """

    def __init__(self, host: str | None = None, model: str | None = None) -> None:
        self._host = (
            (host or os.getenv("OLLAMA_BASE_URL") or os.getenv("OLLAMA_HOST"))
            or "http://localhost:11434"
        ).rstrip("/")
        # Fail closed before the Ollama SDK client is created (D5 / O1).
        validate_model_provider_endpoint(self._host)
        self._model = (
            model or os.getenv("OLLAMA_MODEL") or os.getenv("OLLAMA_MODEL_EMBEDDING")
        ) or "qwen3-embedding"
        dims_env = os.getenv("OLLAMA_EMBEDDING_DIMS", "1536")
        self._dims: int = int(dims_env)
        self._request_timeout_seconds = _get_positive_float_env(
            "EMBEDDING_REQUEST_TIMEOUT_SECONDS",
            _DEFAULT_REQUEST_TIMEOUT_SECONDS,
        )
        self._health_timeout_seconds = _get_positive_float_env(
            "EMBEDDING_HEALTH_TIMEOUT_SECONDS",
            _DEFAULT_HEALTH_TIMEOUT_SECONDS,
        )
        self._client: ollama.AsyncClient = ollama.AsyncClient(host=self._host)
        self._last_health_status: str | None = None
        self._last_health_time: float = 0.0

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    async def generate_embedding(self, text: str) -> list[float]:
        """Return a 1536-dimensional embedding for *text*.

        Retries up to 3 times with exponential back-off on transient failures.
        Raises ``ValueError`` if the returned vector length != 1536.
        """
        last_error: Exception | None = None

        for attempt in range(_MAX_RETRIES):
            try:
                response = await asyncio.wait_for(
                    self._client.embed(
                        model=self._model,
                        input=text,
                        dimensions=self._dims,
                    ),
                    timeout=self._request_timeout_seconds,
                )

                vector: list[float] = list(response.embeddings[0])

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

        Results are cached for up to 60 seconds.  During startup (first call)
        the server may not have loaded the model yet; once ``connected`` the
        cached value is returned without additional network calls.

        This method **never** raises – it is safe to call from health-check
        endpoints.
        """
        now = time.monotonic()
        if (
            self._last_health_status is not None
            and (now - self._last_health_time) < _HEALTH_CACHE_TTL_SECONDS
        ):
            return self._last_health_status

        try:
            await asyncio.wait_for(
                self._client.list(),
                timeout=self._health_timeout_seconds,
            )
            self._last_health_status = "connected"
        except Exception:
            self._last_health_status = "disconnected"

        self._last_health_time = now
        return self._last_health_status

    async def check_status(self) -> str:
        return await self.check_ollama_status()
