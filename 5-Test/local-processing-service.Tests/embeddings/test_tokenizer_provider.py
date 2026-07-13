"""Unit tests for embeddings.tokenizer_provider.

All filesystem interactions are mocked so the tests run without an actual
LM Studio installation or Hugging Face model cache.
"""

from __future__ import annotations

import os
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import MagicMock, patch

import pytest

from embeddings.tokenizer_provider import (
    DEFAULT_LM_STUDIO_MODELS_DIR,
    TOKENIZER_INDICATOR_FILES,
    TokenizerConfigurationError,
    TokenizerResolution,
    _find_lm_studio_model,
    _has_tokenizer_files,
    _normalize_model_name,
    _reset_tokenizer_cache,
    _validate_tokenizer_path,
    describe_chunker_tokenizer,
    get_pdf_chunker_tokenizer,
    resolve_chunker_tokenizer,
)


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _make_resolution(
    model: str = "some/model",
    source: str = "EMBEDDING_MODEL",
    path: str | None = None,
) -> TokenizerResolution:
    return TokenizerResolution(model=model, source=source, path=path)


@pytest.fixture(autouse=True)
def _reset_cache():
    """Ensure the module-level tokenizer cache is cleared between tests."""
    _reset_tokenizer_cache()
    yield
    _reset_tokenizer_cache()


# ---------------------------------------------------------------------------
# _normalize_model_name
# ---------------------------------------------------------------------------


class TestNormalizeModelName:
    def test_strips_non_alphanumeric(self):
        assert _normalize_model_name("Qwen/Qwen3-Embedding-0.6B") == "qwenqwen3embedding06b"

    def test_empty_string(self):
        assert _normalize_model_name("") == ""

    def test_already_lowercase_alphanumeric(self):
        assert _normalize_model_name("qwen3embedding") == "qwen3embedding"

    def test_uppercase_letters_lowercased(self):
        assert _normalize_model_name("Qwen3") == "qwen3"


# ---------------------------------------------------------------------------
# _has_tokenizer_files
# ---------------------------------------------------------------------------


class TestHasTokenizerFiles:
    def test_returns_false_for_missing_path(self, tmp_path):
        assert _has_tokenizer_files(tmp_path / "nonexistent") is False

    def test_returns_true_for_file_named_tokenizer_json(self, tmp_path):
        f = tmp_path / "tokenizer.json"
        f.write_text("{}")
        assert _has_tokenizer_files(f) is True

    def test_returns_false_for_file_not_in_set(self, tmp_path):
        f = tmp_path / "config.json"
        f.write_text("{}")
        assert _has_tokenizer_files(f) is False

    def test_returns_true_for_directory_containing_tokenizer_json(self, tmp_path):
        (tmp_path / "tokenizer.json").write_text("{}")
        assert _has_tokenizer_files(tmp_path) is True

    def test_returns_true_for_directory_containing_tokenizer_model(self, tmp_path):
        (tmp_path / "tokenizer.model").write_bytes(b"\x00")
        assert _has_tokenizer_files(tmp_path) is True

    def test_returns_false_for_empty_directory(self, tmp_path):
        assert _has_tokenizer_files(tmp_path) is False


# ---------------------------------------------------------------------------
# _validate_tokenizer_path
# ---------------------------------------------------------------------------


class TestValidateTokenizerPath:
    def test_raises_when_path_has_no_tokenizer_files(self, tmp_path):
        with pytest.raises(TokenizerConfigurationError, match="TOKENIZER_MODEL_PATH"):
            _validate_tokenizer_path(str(tmp_path), "TOKENIZER_MODEL_PATH")

    def test_returns_resolution_when_valid(self, tmp_path):
        (tmp_path / "tokenizer.json").write_text("{}")
        result = _validate_tokenizer_path(str(tmp_path), "TOKENIZER_MODEL_PATH")
        assert result.source == "TOKENIZER_MODEL_PATH"
        assert result.path == str(tmp_path)

    def test_expands_home_tilde(self, tmp_path, monkeypatch):
        """~/ should be expanded to home dir before checking files."""
        (tmp_path / "tokenizer.json").write_text("{}")
        monkeypatch.setenv("HOME", str(tmp_path))
        result = _validate_tokenizer_path("~/", "TOKENIZER_MODEL_PATH")
        assert result.path is not None


# ---------------------------------------------------------------------------
# _find_lm_studio_model
# ---------------------------------------------------------------------------


class TestFindLmStudioModel:
    def test_returns_none_when_models_dir_missing(self, tmp_path):
        missing = tmp_path / "no_such_dir"
        assert _find_lm_studio_model("qwen3-embedding", missing) is None

    def test_finds_exact_match_by_folder_name(self, tmp_path):
        model_dir = tmp_path / "qwen3-embedding"
        model_dir.mkdir()
        (model_dir / "tokenizer.json").write_text("{}")
        result = _find_lm_studio_model("qwen3-embedding", tmp_path)
        assert result == model_dir

    def test_finds_nested_match_by_partial_name(self, tmp_path):
        """
        LM Studio stores models as <publisher>/<model-name>/<files>.
        A model id like 'Qwen/Qwen3-Embedding-0.6B' should match a nested
        folder whose name contains 'qwen3embedding06b' or a prefix of it.
        """
        nested = tmp_path / "Qwen" / "Qwen3-Embedding-0.6B-GGUF"
        nested.mkdir(parents=True)
        (nested / "tokenizer.json").write_text("{}")
        result = _find_lm_studio_model("Qwen/Qwen3-Embedding-0.6B", tmp_path)
        assert result == nested

    def test_returns_none_when_no_tokenizer_files_match(self, tmp_path):
        other = tmp_path / "OtherModel"
        other.mkdir()
        (other / "config.json").write_text("{}")
        result = _find_lm_studio_model("qwen3-embedding", tmp_path)
        assert result is None

    def test_returns_none_for_empty_model_id(self, tmp_path):
        assert _find_lm_studio_model("", tmp_path) is None

    def test_prefers_shallower_path_when_multiple_candidates(self, tmp_path):
        shallow = tmp_path / "Qwen3-Embedding"
        shallow.mkdir()
        (shallow / "tokenizer.json").write_text("{}")

        deep = tmp_path / "Qwen3-Embedding" / "extras" / "Qwen3-Embedding-sub"
        deep.mkdir(parents=True)
        (deep / "tokenizer.json").write_text("{}")

        result = _find_lm_studio_model("Qwen3-Embedding", tmp_path)
        assert result == shallow


# ---------------------------------------------------------------------------
# resolve_chunker_tokenizer
# ---------------------------------------------------------------------------


class TestResolveChunkerTokenizer:
    def test_uses_explicit_tokenizer_model_path(self, tmp_path, monkeypatch):
        (tmp_path / "tokenizer.json").write_text("{}")
        monkeypatch.setenv("TOKENIZER_MODEL_PATH", str(tmp_path))
        result = resolve_chunker_tokenizer()
        assert result.source == "TOKENIZER_MODEL_PATH"
        assert result.path == str(tmp_path)

    def test_tokenizer_model_path_raises_when_path_invalid(self, tmp_path, monkeypatch):
        empty = tmp_path / "empty_dir"
        empty.mkdir()
        monkeypatch.setenv("TOKENIZER_MODEL_PATH", str(empty))
        with pytest.raises(TokenizerConfigurationError):
            resolve_chunker_tokenizer()

    def test_uses_pdf_chunker_tokenizer_legacy_override(self, monkeypatch):
        monkeypatch.delenv("TOKENIZER_MODEL_PATH", raising=False)
        monkeypatch.setenv("PDF_CHUNKER_TOKENIZER", "Qwen/Qwen3-Embedding-0.6B")
        result = resolve_chunker_tokenizer()
        assert result.source == "PDF_CHUNKER_TOKENIZER"
        assert result.model == "Qwen/Qwen3-Embedding-0.6B"

    def test_pdf_chunker_tokenizer_sets_path_none_for_hf_id(self, monkeypatch):
        """A HuggingFace id string that is not a real path should have path=None."""
        monkeypatch.delenv("TOKENIZER_MODEL_PATH", raising=False)
        monkeypatch.setenv("PDF_CHUNKER_TOKENIZER", "Qwen/Qwen3-Embedding-0.6B")
        result = resolve_chunker_tokenizer()
        # The path attribute should be None because the string is not a real dir
        assert result.path is None

    def test_falls_through_to_embedding_model(self, tmp_path, monkeypatch):
        monkeypatch.delenv("TOKENIZER_MODEL_PATH", raising=False)
        monkeypatch.delenv("PDF_CHUNKER_TOKENIZER", raising=False)
        model_dir = tmp_path / "qwen3-embedding"
        model_dir.mkdir()
        (model_dir / "tokenizer.json").write_text("{}")
        monkeypatch.setenv("EMBEDDING_MODEL", "qwen3-embedding")
        monkeypatch.setenv("LM_STUDIO_MODELS_DIR", str(tmp_path))
        result = resolve_chunker_tokenizer()
        assert result.source == "LM_STUDIO_MODELS_DIR"
        assert result.path == str(model_dir)

    def test_falls_through_to_ollama_model(self, tmp_path, monkeypatch):
        monkeypatch.delenv("TOKENIZER_MODEL_PATH", raising=False)
        monkeypatch.delenv("PDF_CHUNKER_TOKENIZER", raising=False)
        monkeypatch.delenv("EMBEDDING_MODEL", raising=False)
        model_dir = tmp_path / "qwen3-embedding"
        model_dir.mkdir()
        (model_dir / "tokenizer.json").write_text("{}")
        monkeypatch.setenv("OLLAMA_MODEL", "qwen3-embedding")
        monkeypatch.setenv("LM_STUDIO_MODELS_DIR", str(tmp_path))
        result = resolve_chunker_tokenizer()
        assert result.source == "LM_STUDIO_MODELS_DIR"

    def test_raises_when_no_model_configured(self, monkeypatch):
        monkeypatch.delenv("TOKENIZER_MODEL_PATH", raising=False)
        monkeypatch.delenv("PDF_CHUNKER_TOKENIZER", raising=False)
        monkeypatch.delenv("EMBEDDING_MODEL", raising=False)
        monkeypatch.delenv("OLLAMA_MODEL", raising=False)
        with pytest.raises(TokenizerConfigurationError, match="No tokenizer model"):
            resolve_chunker_tokenizer()

    def test_raises_when_model_not_found_locally_and_no_slash(self, tmp_path, monkeypatch):
        """A short name like 'mymodel' with no '/' is not a HF id → error."""
        monkeypatch.delenv("TOKENIZER_MODEL_PATH", raising=False)
        monkeypatch.delenv("PDF_CHUNKER_TOKENIZER", raising=False)
        monkeypatch.setenv("EMBEDDING_MODEL", "mymodel-no-slash")
        monkeypatch.setenv("LM_STUDIO_MODELS_DIR", str(tmp_path))
        # tmp_path is empty, no match
        with pytest.raises(TokenizerConfigurationError, match="Unable to resolve"):
            resolve_chunker_tokenizer()

    def test_hf_id_with_slash_resolves_without_local_files(self, tmp_path, monkeypatch):
        """A HF-style org/model id should resolve even without local files."""
        monkeypatch.delenv("TOKENIZER_MODEL_PATH", raising=False)
        monkeypatch.delenv("PDF_CHUNKER_TOKENIZER", raising=False)
        monkeypatch.setenv("EMBEDDING_MODEL", "Qwen/Qwen3-Embedding-0.6B")
        monkeypatch.setenv("LM_STUDIO_MODELS_DIR", str(tmp_path))
        result = resolve_chunker_tokenizer()
        assert result.source == "EMBEDDING_MODEL"
        assert result.path is None


# ---------------------------------------------------------------------------
# describe_chunker_tokenizer
# ---------------------------------------------------------------------------


class TestDescribeChunkerTokenizer:
    def test_returns_configured_status_when_resolved(self, tmp_path, monkeypatch):
        (tmp_path / "tokenizer.json").write_text("{}")
        monkeypatch.setenv("TOKENIZER_MODEL_PATH", str(tmp_path))
        result = describe_chunker_tokenizer()
        assert result["tokenizer_status"] == "configured"
        assert result["tokenizer_model"] is not None
        assert result["tokenizer_source"] == "TOKENIZER_MODEL_PATH"

    def test_returns_missing_status_when_no_model(self, monkeypatch):
        monkeypatch.delenv("TOKENIZER_MODEL_PATH", raising=False)
        monkeypatch.delenv("PDF_CHUNKER_TOKENIZER", raising=False)
        monkeypatch.delenv("EMBEDDING_MODEL", raising=False)
        monkeypatch.delenv("OLLAMA_MODEL", raising=False)
        result = describe_chunker_tokenizer()
        assert result["tokenizer_status"] == "missing"
        assert result["tokenizer_model"] is None
        assert "tokenizer_error" in result


# ---------------------------------------------------------------------------
# get_pdf_chunker_tokenizer
# ---------------------------------------------------------------------------


class TestGetPdfChunkerTokenizer:
    @patch("embeddings.tokenizer_provider.HuggingFaceTokenizer")
    def test_calls_from_pretrained_with_resolved_model(self, MockTokenizer, tmp_path, monkeypatch):
        (tmp_path / "tokenizer.json").write_text("{}")
        monkeypatch.setenv("TOKENIZER_MODEL_PATH", str(tmp_path))

        mock_instance = MagicMock()
        MockTokenizer.from_pretrained.return_value = mock_instance

        result = get_pdf_chunker_tokenizer(512)

        MockTokenizer.from_pretrained.assert_called_once_with(
            model_name=str(tmp_path),
            max_tokens=512,
            trust_remote_code=True,
            local_files_only=True,  # path is not None → local_files_only=True
        )
        assert result is mock_instance

    @patch("embeddings.tokenizer_provider.HuggingFaceTokenizer")
    def test_local_files_only_false_for_hf_id(self, MockTokenizer, tmp_path, monkeypatch):
        """When path is None (HF id download), local_files_only must be False."""
        monkeypatch.delenv("TOKENIZER_MODEL_PATH", raising=False)
        monkeypatch.delenv("PDF_CHUNKER_TOKENIZER", raising=False)
        monkeypatch.setenv("EMBEDDING_MODEL", "Qwen/Qwen3-Embedding-0.6B")
        monkeypatch.setenv("LM_STUDIO_MODELS_DIR", str(tmp_path))

        mock_instance = MagicMock()
        MockTokenizer.from_pretrained.return_value = mock_instance

        get_pdf_chunker_tokenizer(256)

        _, kwargs = MockTokenizer.from_pretrained.call_args
        assert kwargs["local_files_only"] is False

    @patch("embeddings.tokenizer_provider.HuggingFaceTokenizer")
    def test_uses_default_max_tokens_when_zero(self, MockTokenizer, tmp_path, monkeypatch):
        (tmp_path / "tokenizer.json").write_text("{}")
        monkeypatch.setenv("TOKENIZER_MODEL_PATH", str(tmp_path))
        MockTokenizer.from_pretrained.return_value = MagicMock()

        get_pdf_chunker_tokenizer(0)

        from embeddings.tokenizer_provider import DEFAULT_TOKENIZER_CONTEXT_TOKENS
        _, kwargs = MockTokenizer.from_pretrained.call_args
        assert kwargs["max_tokens"] == DEFAULT_TOKENIZER_CONTEXT_TOKENS
