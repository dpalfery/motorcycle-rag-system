"""Helpers for detecting embedding providers and listing available models."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

import httpx

from security.safe_http import (
    RedirectBlockedError,
    create_model_discovery_async_client,
    create_model_discovery_client,
    require_non_redirect_success,
    validate_model_discovery_endpoint,
)

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

    for suffix in ("/models", "/embeddings", "/chat/completions", "/api/tags"):
        if normalized.lower().endswith(suffix):
            normalized = normalized[: -len(suffix)]
            break

    validate_model_discovery_endpoint(normalized)
    return normalized


def _unique(values: list[str]) -> list[str]:
    seen: set[str] = set()
    unique_values: list[str] = []
    for value in values:
        if value not in seen:
            seen.add(value)
            unique_values.append(value)
    return unique_values


def _unique_urls(values: list[httpx.URL]) -> list[httpx.URL]:
    seen: set[str] = set()
    unique_values: list[httpx.URL] = []
    for value in values:
        key = str(value)
        if key not in seen:
            seen.add(key)
            unique_values.append(value)
    return unique_values


def _validated_discovery_request_url(url: httpx.URL | str) -> httpx.URL:
    """Return a policy-validated absolute discovery URL.

    Re-runs ``validate_model_discovery_endpoint`` so only public-HTTPS or
    literal-loopback HTTP authorities reach ``client.get`` (D4 / safe_http).
    """
    validated, _policy = validate_model_discovery_endpoint(str(url))
    return validated


def _join_discovery_path(base_url: httpx.URL, relative_path: str) -> httpx.URL:
    """Join a relative path onto a validated base without replacing the last segment.

    ``httpx.URL.join`` follows RFC 3986: a base path without a trailing slash
    treats the final segment as replaceable. Discovery bases such as
    ``.../v1`` must append ``models`` as ``.../v1/models``, so normalize the
    base path to a directory form before joining.
    """
    directory_base = base_url.copy_with(path=f"{base_url.path.rstrip('/')}/")
    return _validated_discovery_request_url(
        directory_base.join(relative_path.lstrip("/"))
    )


def _openai_candidate_urls(endpoint: str) -> list[httpx.URL]:
    base = _normalize_endpoint(endpoint)
    base_url, _policy = validate_model_discovery_endpoint(base)
    candidates: list[httpx.URL] = []
    if not base.lower().endswith("/v1"):
        candidates.append(_join_discovery_path(base_url, "v1/models"))
    candidates.append(_join_discovery_path(base_url, "models"))
    return _unique_urls(candidates)


def _ollama_candidate_urls(endpoint: str) -> list[httpx.URL]:
    base = _normalize_endpoint(endpoint)
    base_url, _policy = validate_model_discovery_endpoint(base)
    return [_join_discovery_path(base_url, "api/tags")]


def _candidate_base_url(url: httpx.URL | str, suffix: str) -> str:
    url_str = str(url)
    return url_str[: -len(suffix)] if url_str.lower().endswith(suffix.lower()) else url_str


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
        if isinstance(item, dict)
        and str(item.get("model") or item.get("name") or "").strip()
    ]
    return _unique(models)


def _sync_get_json(
    client: httpx.Client,
    url: str | httpx.URL,
    headers: dict[str, str] | None = None,
) -> Any:
    # Re-validate before every GET so sync/async paths share the same barrier.
    validated_url = _validated_discovery_request_url(url)
    response = client.get(validated_url, headers=headers)
    require_non_redirect_success(response)
    return response.json()


async def _async_get_json(
    client: httpx.AsyncClient,
    url: str | httpx.URL,
    headers: dict[str, str] | None = None,
) -> Any:
    # codeql[py/partial-ssrf]: operator-supplied provider endpoints are probed by
    # design (D4). validate_model_discovery_endpoint() rejects non-public /
    # non-loopback hosts at this call site and again when candidates are built;
    # create_model_discovery_async_client()'s _SafeAsyncTransport re-validates
    # every DNS answer before dialing and blocks redirects — see safe_http.py.
    validated_url = _validated_discovery_request_url(url)
    response = await client.get(validated_url, headers=headers)
    require_non_redirect_success(response)
    return response.json()


def _discover_with_sync_client(
    client: httpx.Client, endpoint: str
) -> ModelDiscoveryResult:
    last_error: Exception | None = None

    for url in _openai_candidate_urls(endpoint):
        # _LOCAL_PLACEHOLDER_API_KEY is a non-secret placeholder for
        # unauthenticated local OpenAI-compatible servers (LM Studio, Ollama /v1,
        # Foundry Local) that ignore the bearer value.
        for headers in (
            None,
            {"Authorization": f"Bearer {_LOCAL_PLACEHOLDER_API_KEY}"},
        ):
            try:
                payload = _sync_get_json(client, url, headers=headers)
                models = _parse_openai_payload(payload)
                if models:
                    return ModelDiscoveryResult(
                        "openai-compatible", _candidate_base_url(url, "/models"), models
                    )
            except RedirectBlockedError:
                raise
            except Exception as exc:  # noqa: BLE001
                last_error = exc

    for url in _ollama_candidate_urls(endpoint):
        try:
            payload = _sync_get_json(client, url)
            models = _parse_ollama_payload(payload)
            if models:
                return ModelDiscoveryResult(
                    "ollama", _candidate_base_url(url, "/api/tags"), models
                )
        except RedirectBlockedError:
            raise
        except Exception as exc:  # noqa: BLE001
            last_error = exc

    raise ModelDiscoveryError(
        f"Unable to detect an embedding model provider at {_normalize_endpoint(endpoint)}"
    ) from last_error


async def _discover_with_async_client(
    client: httpx.AsyncClient, endpoint: str
) -> ModelDiscoveryResult:
    last_error: Exception | None = None

    for url in _openai_candidate_urls(endpoint):
        # _LOCAL_PLACEHOLDER_API_KEY is a non-secret placeholder for
        # unauthenticated local OpenAI-compatible servers (LM Studio, Ollama /v1,
        # Foundry Local) that ignore the bearer value.
        for headers in (
            None,
            {"Authorization": f"Bearer {_LOCAL_PLACEHOLDER_API_KEY}"},
        ):
            try:
                payload = await _async_get_json(client, url, headers=headers)
                models = _parse_openai_payload(payload)
                if models:
                    return ModelDiscoveryResult(
                        "openai-compatible", _candidate_base_url(url, "/models"), models
                    )
            except RedirectBlockedError:
                raise
            except Exception as exc:  # noqa: BLE001
                last_error = exc

    for url in _ollama_candidate_urls(endpoint):
        try:
            payload = await _async_get_json(client, url)
            models = _parse_ollama_payload(payload)
            if models:
                return ModelDiscoveryResult(
                    "ollama", _candidate_base_url(url, "/api/tags"), models
                )
        except RedirectBlockedError:
            raise
        except Exception as exc:  # noqa: BLE001
            last_error = exc

    raise ModelDiscoveryError(
        f"Unable to detect an embedding model provider at {_normalize_endpoint(endpoint)}"
    ) from last_error


def discover_embedding_models_sync(endpoint: str) -> ModelDiscoveryResult:
    _, policy = validate_model_discovery_endpoint(endpoint)
    with create_model_discovery_client(policy) as client:
        return _discover_with_sync_client(client, endpoint)


async def discover_embedding_models(endpoint: str) -> ModelDiscoveryResult:
    _, policy = validate_model_discovery_endpoint(endpoint)
    async with create_model_discovery_async_client(policy) as client:
        return await _discover_with_async_client(client, endpoint)
