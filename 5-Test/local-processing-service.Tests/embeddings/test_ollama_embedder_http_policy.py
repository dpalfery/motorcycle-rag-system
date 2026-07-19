"""Contract tests: OllamaEmbedder must validate host at construct (plan T3/T6, D5 O1).

Ollama traffic stays on ``ollama.AsyncClient``; only construct-time endpoint
policy validation is in scope. These tests fail (red) until T6 validates
OLLAMA_BASE_URL / OLLAMA_HOST / factory-supplied host before SDK client creation.
"""

from __future__ import annotations

from unittest.mock import patch

import pytest

from security.safe_http import EndpointPolicyError

_UNSAFE_OLLAMA_HOSTS = (
    "http://models.example.test:11434",
    "http://127.0.0.1.example.test:11434",
    "http://10.0.0.1:11434",
    "http://192.168.1.1:11434",
)

_POLICY_ALLOWED_OLLAMA_HOSTS = (
    "http://localhost:11434",
    "http://127.0.0.1:11434",
    "http://[::1]:11434",
    "https://models.example.test:11434",
)


class TestOllamaEmbedderConstructEndpointPolicy:
    @pytest.mark.parametrize("host", _UNSAFE_OLLAMA_HOSTS)
    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    def test_construct_when_http_is_not_literal_loopback_rejects(
        self,
        _mock_async_client,
        host: str,
    ) -> None:
        from embeddings.ollama_embedder import OllamaEmbedder

        with pytest.raises(EndpointPolicyError):
            OllamaEmbedder(host=host)

    @pytest.mark.parametrize("host", _UNSAFE_OLLAMA_HOSTS)
    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    def test_construct_when_env_host_is_not_literal_loopback_rejects(
        self,
        _mock_async_client,
        monkeypatch: pytest.MonkeyPatch,
        host: str,
    ) -> None:
        monkeypatch.setenv("OLLAMA_BASE_URL", host)

        from embeddings.ollama_embedder import OllamaEmbedder

        with pytest.raises(EndpointPolicyError):
            OllamaEmbedder()

    @pytest.mark.parametrize("host", _UNSAFE_OLLAMA_HOSTS)
    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    def test_construct_when_legacy_ollama_host_env_is_not_literal_loopback_rejects(
        self,
        _mock_async_client,
        monkeypatch: pytest.MonkeyPatch,
        host: str,
    ) -> None:
        monkeypatch.delenv("OLLAMA_BASE_URL", raising=False)
        monkeypatch.setenv("OLLAMA_HOST", host)

        from embeddings.ollama_embedder import OllamaEmbedder

        with pytest.raises(EndpointPolicyError):
            OllamaEmbedder()

    @pytest.mark.parametrize("host", _POLICY_ALLOWED_OLLAMA_HOSTS)
    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    def test_construct_when_host_is_policy_allowed_succeeds(
        self,
        mock_async_client,
        host: str,
    ) -> None:
        from embeddings.ollama_embedder import OllamaEmbedder

        embedder = OllamaEmbedder(host=host)

        assert embedder._host == host.rstrip("/")
        mock_async_client.assert_called_once_with(host=host.rstrip("/"))
