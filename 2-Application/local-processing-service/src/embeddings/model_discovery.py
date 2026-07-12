"""Helpers for detecting embedding providers and listing available models."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

import httpx

# Public, non-secret placeholder for local OpenAI-compatible servers
# (LM Studio, Ollama /v1, Foundry Local) that do not require authentication.
# These servers ignore the bearer value; the OpenAI client only requires a
# non-empty string. Set EMBEDDING_PROVIDER_API_KEY when calling an
# authenticated cloud endpoint. NOT a credential — do not treat as a secret.
_LOCAL_PLACEHOLDER_API_KEY = "local"


class ModelDiscoveryError(RuntimeError):
    """Raised when a provider endpoint cannot be identified or queried."""


@dataclass(frozen=True)
class ModelDiscoveryResult:
    provider: str
    endpoint: str
    models: list[str]


def _normalize_endpoint(endpoint: str) -> str:
    normalized = endpoint.strip().rstrip("/")
    if not normalized:
        raise ValueError("Embedding provider endpoint is required")

    for suffix in ("/models", "/embeddings", "/chat/completions", "/api/tags"):
        if normalized.lower().endswith(suffix):
            normalized = normalized[: -len(suffix)]
            break

    return normalized


def _unique(values: list[str]) -> list[str]:
    seen: set[str] = set()
    unique_values: list[str] = []
    for value in values:
        if value not in seen:
            seen.add(value)
            unique_values.append(value)
    return unique_values


def _openai_candidate_urls(endpoint: str) -> list[str]:
    base = _normalize_endpoint(endpoint)
    candidates: list[str] = []
    if not base.lower().endswith("/v1"):
        candidates.append(f"{base}/v1/models")
    candidates.append(f"{base}/models")
    return _unique(candidates)


def _ollama_candidate_urls(endpoint: str) -> list[str]:
    return [f"{_normalize_endpoint(endpoint)}/api/tags"]


def _candidate_base_url(url: str, suffix: str) -> str:
    return url[: -len(suffix)] if url.lower().endswith(suffix.lower()) else url


def _parse_openai_payload(payload: Any) -> list[str]:
    if not isinstance(payload, dict):
        raise ValueError("OpenAI-compatible payload must be a JSON object")

    data = payload.get("data")
    if not isinstance(data, list):
        raise ValueError("OpenAI-compatible payload is missing a data array")

    models = [
        str(item.get("id", "")).strip()
        for item in data
        if isinstance(item, dict) and str(item.get("id", "")).strip()
    ]
    return _unique(models)


def _parse_ollama_payload(payload: Any) -> list[str]:
    if not isinstance(payload, dict):
        raise ValueError("Ollama payload must be a JSON object")

    data = payload.get("models")
    if not isinstance(data, list):
        raise ValueError("Ollama payload is missing a models array")

    models = [
        str(item.get("model") or item.get("name") or "").strip()
        for item in data
        if isinstance(item, dict) and str(item.get("model") or item.get("name") or "").strip()
    ]
    return _unique(models)


def _sync_get_json(client: httpx.Client, url: str, headers: dict[str, str] | None = None) -> Any:
    response = client.get(url, headers=headers)
    response.raise_for_status()
    return response.json()


async def _async_get_json(
    client: httpx.AsyncClient, url: str, headers: dict[str, str] | None = None
) -> Any:
    response = await client.get(url, headers=headers)
    response.raise_for_status()
    return response.json()


def _discover_with_sync_client(client: httpx.Client, endpoint: str) -> ModelDiscoveryResult:
    last_error: Exception | None = None

    for url in _openai_candidate_urls(endpoint):
        # _LOCAL_PLACEHOLDER_API_KEY is a non-secret placeholder for
        # unauthenticated local OpenAI-compatible servers (LM Studio, Ollama /v1,
        # Foundry Local) that ignore the bearer value.
        for headers in (None, {"Authorization": f"Bearer {_LOCAL_PLACEHOLDER_API_KEY}"}):
            try:
                payload = _sync_get_json(client, url, headers=headers)
                models = _parse_openai_payload(payload)
                if models:
                    return ModelDiscoveryResult("openai-compatible", _candidate_base_url(url, "/models"), models)
            except Exception as exc:  # noqa: BLE001
                last_error = exc

    for url in _ollama_candidate_urls(endpoint):
        try:
            payload = _sync_get_json(client, url)
            models = _parse_ollama_payload(payload)
            if models:
                return ModelDiscoveryResult("ollama", _candidate_base_url(url, "/api/tags"), models)
        except Exception as exc:  # noqa: BLE001
            last_error = exc

    raise ModelDiscoveryError(
        f"Unable to detect an embedding model provider at {_normalize_endpoint(endpoint)}"
    ) from last_error


async def _discover_with_async_client(client: httpx.AsyncClient, endpoint: str) -> ModelDiscoveryResult:
    last_error: Exception | None = None

    for url in _openai_candidate_urls(endpoint):
        # _LOCAL_PLACEHOLDER_API_KEY is a non-secret placeholder for
        # unauthenticated local OpenAI-compatible servers (LM Studio, Ollama /v1,
        # Foundry Local) that ignore the bearer value.
        for headers in (None, {"Authorization": f"Bearer {_LOCAL_PLACEHOLDER_API_KEY}"}):
            try:
                payload = await _async_get_json(client, url, headers=headers)
                models = _parse_openai_payload(payload)
                if models:
                    return ModelDiscoveryResult("openai-compatible", _candidate_base_url(url, "/models"), models)
            except Exception as exc:  # noqa: BLE001
                last_error = exc

    for url in _ollama_candidate_urls(endpoint):
        try:
            payload = await _async_get_json(client, url)
            models = _parse_ollama_payload(payload)
            if models:
                return ModelDiscoveryResult("ollama", _candidate_base_url(url, "/api/tags"), models)
        except Exception as exc:  # noqa: BLE001
            last_error = exc

    raise ModelDiscoveryError(
        f"Unable to detect an embedding model provider at {_normalize_endpoint(endpoint)}"
    ) from last_error


def discover_embedding_models_sync(endpoint: str) -> ModelDiscoveryResult:
    with httpx.Client(timeout=httpx.Timeout(10.0)) as client:
        return _discover_with_sync_client(client, endpoint)


async def discover_embedding_models(endpoint: str) -> ModelDiscoveryResult:
    async with httpx.AsyncClient(timeout=httpx.Timeout(10.0)) as client:
        return await _discover_with_async_client(client, endpoint)