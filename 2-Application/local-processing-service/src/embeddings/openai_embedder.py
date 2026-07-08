"""OpenAI-compatible embedding client for any OpenAI-compatible server (LM Studio, Ollama /v1, Foundry Local, etc.)."""

from __future__ import annotations

import asyncio
import logging
import os
import time

import httpx
import openai

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


def _normalize_openai_base_url(endpoint: str) -> str:
    normalized = endpoint.strip().rstrip("/")
    if not normalized:
        return ""

    for suffix in ("/models", "/embeddings", "/chat/completions"):
        if normalized.lower().endswith(suffix):
            normalized = normalized[: -len(suffix)]
            break

    return normalized


class OpenAIEmbedder(Embedder):
    """Generates embeddings via any OpenAI-compatible API.

    Constructor parameters take precedence; when omitted, values are read
    from environment variables:

        EMBEDDING_PROVIDER_ENDPOINT  – server URL    (default: http://localhost:1234/v1)
        EMBEDDING_MODEL             – model name    (default: qwen3-embedding)
        EMBEDDING_PROVIDER_API_KEY  – Bearer token  (optional; uses ``"local"`` when unset)
        EMBEDDING_DIMS              – expected dims (default: 1536)

    Enforces a configurable dimension check to match the Azure AI Search
    index (VectorSearchDimensions).  Set ``EMBEDDING_DIMS`` or leave unset
    to use the default of 1536.
    """

    def __init__(
        self,
        endpoint: str | None = None,
        model: str | None = None,
        api_key: str | None = None,
        dims: int | None = None,
        request_timeout_seconds: float | None = None,
        health_timeout_seconds: float | None = None,
    ) -> None:
        self._endpoint: str = _normalize_openai_base_url(
            endpoint
            or os.getenv("EMBEDDING_PROVIDER_ENDPOINT")
            or "http://localhost:1234/v1"
        )
        self._model: str = (
            model
            or os.getenv("EMBEDDING_MODEL")
            or "qwen3-embedding"
        )
        self._api_key: str = (
            api_key
            or os.getenv("EMBEDDING_PROVIDER_API_KEY")
            or "local"
        )
        self._dims: int = dims or int(os.getenv("EMBEDDING_DIMS", "1536"))
        self._request_timeout_seconds = request_timeout_seconds or _get_positive_float_env(
            "EMBEDDING_REQUEST_TIMEOUT_SECONDS",
            _DEFAULT_REQUEST_TIMEOUT_SECONDS,
        )
        self._health_timeout_seconds = health_timeout_seconds or _get_positive_float_env(
            "EMBEDDING_HEALTH_TIMEOUT_SECONDS",
            _DEFAULT_HEALTH_TIMEOUT_SECONDS,
        )
        self._last_health_status: str | None = None
        self._last_health_time: float = 0.0

    def _create_client(self) -> openai.AsyncOpenAI:
        return openai.AsyncOpenAI(
            base_url=self._endpoint,
            api_key=self._api_key,
        )

    async def _close_client(self, client: openai.AsyncOpenAI) -> None:
        close = getattr(client, "close", None)
        if close is None:
            return

        result = close()
        if asyncio.iscoroutine(result):
            await result

    async def generate_embedding(self, text: str) -> list[float]:
        last_error: Exception | None = None

        for attempt in range(_MAX_RETRIES):
            client = self._create_client()
            try:
                response = await asyncio.wait_for(
                    client.embeddings.create(
                        model=self._model,
                        input=text,
                        dimensions=self._dims,
                    ),
                    timeout=self._request_timeout_seconds,
                )

                vector: list[float] = response.data[0].embedding

                if len(vector) != self._dims:
                    raise ValueError(f"Expected {self._dims} dims, got {len(vector)}")

                return vector

            except ValueError:
                raise
            except Exception as exc:
                last_error = exc
                logger.warning(
                    "OpenAI embed attempt %d/%d failed: %s",
                    attempt + 1,
                    _MAX_RETRIES,
                    exc,
                )
                if attempt < _MAX_RETRIES - 1:
                    await asyncio.sleep(2**attempt)
            finally:
                await self._close_client(client)

        raise RuntimeError(
            f"OpenAI embedding failed after {_MAX_RETRIES} retries"
        ) from last_error

    async def generate_embeddings_batch(self, texts: list[str]) -> list[list[float]]:
        tasks = [self.generate_embedding(text) for text in texts]
        return list(await asyncio.gather(*tasks))

    async def check_status(self) -> str:
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
