"""Helpers for detecting embedding providers and listing available models."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

import httpx


class ModelDiscoveryError(RuntimeError):
    """Raised when a provider endpoint cannot be identified or queried."""


@dataclass(frozen=True)
class ModelDiscoveryResult:
    provider: str
    models: list[str]


def _normalize_endpoint(endpoint: str) -> str:
    normalized = endpoint.strip().rstrip("/")
    if not normalized:
        raise ValueError("Embedding provider endpoint is required")
    return normalized


def _unique(values: list[str]) -> list[str]:
    seen: set[str] = set()
    unique_values: list[str] = []
    for value in values:
        if value not in seen:
            seen.add(value)
            unique_values.append(value)
    return unique_values


def _prefer_embedding_models(models: list[str]) -> list[str]:
    embedding_models = [
        model for model in models if "embed" in model.lower() or "embedding" in model.lower()
    ]
    return embedding_models if embedding_models else models


def _openai_candidate_urls(endpoint: str) -> list[str]:
    base = _normalize_endpoint(endpoint)
    candidates = [f"{base}/models"]
    if not base.lower().endswith("/v1"):
        candidates.append(f"{base}/v1/models")
    return _unique(candidates)


def _ollama_candidate_urls(endpoint: str) -> list[str]:
    return [f"{_normalize_endpoint(endpoint)}/api/tags"]


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
    return _prefer_embedding_models(_unique(models))


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
    return _prefer_embedding_models(_unique(models))


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
        for headers in (None, {"Authorization": "Bearer local"}):
            try:
                payload = _sync_get_json(client, url, headers=headers)
                models = _parse_openai_payload(payload)
                if models:
                    return ModelDiscoveryResult("openai-compatible", models)
            except Exception as exc:  # noqa: BLE001
                last_error = exc

    for url in _ollama_candidate_urls(endpoint):
        try:
            payload = _sync_get_json(client, url)
            models = _parse_ollama_payload(payload)
            if models:
                return ModelDiscoveryResult("ollama", models)
        except Exception as exc:  # noqa: BLE001
            last_error = exc

    raise ModelDiscoveryError(
        f"Unable to detect an embedding model provider at {_normalize_endpoint(endpoint)}"
    ) from last_error


async def _discover_with_async_client(client: httpx.AsyncClient, endpoint: str) -> ModelDiscoveryResult:
    last_error: Exception | None = None

    for url in _openai_candidate_urls(endpoint):
        for headers in (None, {"Authorization": "Bearer local"}):
            try:
                payload = await _async_get_json(client, url, headers=headers)
                models = _parse_openai_payload(payload)
                if models:
                    return ModelDiscoveryResult("openai-compatible", models)
            except Exception as exc:  # noqa: BLE001
                last_error = exc

    for url in _ollama_candidate_urls(endpoint):
        try:
            payload = await _async_get_json(client, url)
            models = _parse_ollama_payload(payload)
            if models:
                return ModelDiscoveryResult("ollama", models)
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