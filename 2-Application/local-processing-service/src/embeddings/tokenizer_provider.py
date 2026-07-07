"""Tokenizer resolution for Docling chunking.

The embedding provider can be an OpenAI-compatible endpoint such as LM Studio,
but those endpoints do not expose tokenizer APIs. The local processor therefore
loads the matching tokenizer from local model files or Hugging Face metadata.
"""

from __future__ import annotations

import logging
import os
from dataclasses import dataclass
from pathlib import Path

from docling_core.transforms.chunker.tokenizer.huggingface import HuggingFaceTokenizer

logger = logging.getLogger(__name__)


DEFAULT_LM_STUDIO_MODELS_DIR = Path.home() / ".lmstudio" / "models"
DEFAULT_TOKENIZER_CONTEXT_TOKENS = 32768
# Files whose presence in a directory indicates it contains tokenizer data.
# This is a detection heuristic (any-match), not a strict requirement list.
TOKENIZER_INDICATOR_FILES = {
    "tokenizer.json",
    "tokenizer.model",
    "vocab.json",
    "spiece.model",
}

# Module-level cache: the tokenizer is expensive to load and is identical
# for every PDF job within a process lifetime. Reset only in tests.
_tokenizer_cache: HuggingFaceTokenizer | None = None


class TokenizerConfigurationError(RuntimeError):
    """Raised when the configured chunking tokenizer cannot be resolved."""


@dataclass(frozen=True)
class TokenizerResolution:
    """Resolved tokenizer source metadata."""

    model: str
    source: str
    path: str | None


def _normalize_model_name(value: str) -> str:
    return "".join(ch.lower() for ch in value if ch.isalnum())


def _has_tokenizer_files(path: Path) -> bool:
    if path.is_file():
        return path.name in TOKENIZER_INDICATOR_FILES

    if not path.is_dir():
        return False

    return any((path / file_name).is_file() for file_name in TOKENIZER_INDICATOR_FILES)


def _validate_tokenizer_path(path_value: str, source: str) -> TokenizerResolution:
    path = Path(path_value).expanduser()
    if not _has_tokenizer_files(path):
        raise TokenizerConfigurationError(
            f"{source} does not contain tokenizer files: {path}"
        )

    return TokenizerResolution(model=str(path), source=source, path=str(path))


def _find_lm_studio_model(model_id: str, models_dir: Path) -> Path | None:
    exact_path = models_dir / model_id
    if _has_tokenizer_files(exact_path):
        return exact_path

    if not models_dir.is_dir():
        return None

    normalized_model_id = _normalize_model_name(model_id)
    if not normalized_model_id:
        return None

    candidates: list[Path] = []
    for tokenizer_path in models_dir.rglob("tokenizer.*"):
        parent = tokenizer_path.parent
        normalized_parts = [
            _normalize_model_name(part)
            for part in parent.relative_to(models_dir).parts
        ]
        if any(
            part == normalized_model_id
            or normalized_model_id in part
            or part in normalized_model_id
            for part in normalized_parts
        ):
            candidates.append(parent)

    if not candidates:
        return None

    candidates.sort(key=lambda path: (len(path.parts), str(path).lower()))
    return candidates[0]


def resolve_chunker_tokenizer() -> TokenizerResolution:
    """Resolve the tokenizer source for PDF chunking."""

    explicit_path = os.getenv("TOKENIZER_MODEL_PATH", "").strip()
    if explicit_path:
        return _validate_tokenizer_path(explicit_path, "TOKENIZER_MODEL_PATH")

    explicit_tokenizer = os.getenv("PDF_CHUNKER_TOKENIZER", "").strip()
    if explicit_tokenizer:
        return TokenizerResolution(
            model=explicit_tokenizer,
            source="PDF_CHUNKER_TOKENIZER",
            path=explicit_tokenizer if Path(explicit_tokenizer).exists() else None,
        )

    embedding_model = os.getenv("EMBEDDING_MODEL", "").strip()
    if not embedding_model:
        embedding_model = os.getenv("OLLAMA_MODEL", "").strip()
    if not embedding_model:
        embedding_model = os.getenv("AZURE_FOUNDRY_LOCAL_EMBEDDING_MODEL", "").strip()
    if not embedding_model:
        embedding_model = os.getenv("DEEPINFRA_EMBEDDING_MODEL", "").strip()

    if not embedding_model:
        raise TokenizerConfigurationError(
            "No tokenizer model is configured. Set EMBEDDING_MODEL or TOKENIZER_MODEL_PATH."
        )

    models_dir = Path(
        os.getenv("LM_STUDIO_MODELS_DIR", str(DEFAULT_LM_STUDIO_MODELS_DIR))
    ).expanduser()
    local_model_path = _find_lm_studio_model(embedding_model, models_dir)
    if local_model_path is not None:
        return TokenizerResolution(
            model=str(local_model_path),
            source="LM_STUDIO_MODELS_DIR",
            path=str(local_model_path),
        )

    if "/" in embedding_model:
        return TokenizerResolution(
            model=embedding_model,
            source="EMBEDDING_MODEL",
            path=None,
        )

    raise TokenizerConfigurationError(
        "Unable to resolve tokenizer files for EMBEDDING_MODEL "
        f"'{embedding_model}'. Set TOKENIZER_MODEL_PATH or LM_STUDIO_MODELS_DIR."
    )


def describe_chunker_tokenizer() -> dict[str, str | None]:
    """Return tokenizer resolution metadata without loading tokenizer weights."""

    try:
        resolution = resolve_chunker_tokenizer()
        return {
            "tokenizer_status": "configured",
            "tokenizer_model": resolution.model,
            "tokenizer_source": resolution.source,
            "tokenizer_path": resolution.path,
        }
    except TokenizerConfigurationError as exc:
        return {
            "tokenizer_status": "missing",
            "tokenizer_model": None,
            "tokenizer_source": None,
            "tokenizer_path": None,
            "tokenizer_error": str(exc),
        }


def get_pdf_chunker_tokenizer(max_tokens: int) -> HuggingFaceTokenizer:
    """Create (or return cached) Docling tokenizer used by HybridChunker.

    The tokenizer is loaded once per process and then reused. Loading
    HuggingFace tokenizer weights involves I/O and is too expensive to
    repeat on every PDF ingestion task.
    """
    global _tokenizer_cache
    if _tokenizer_cache is not None:
        return _tokenizer_cache

    resolution = resolve_chunker_tokenizer()
    local_files_only = resolution.path is not None
    logger.info(
        "Resolved PDF chunker tokenizer source=%s model=%s path=%s local_files_only=%s max_tokens=%d",
        resolution.source,
        resolution.model,
        resolution.path,
        local_files_only,
        max_tokens or DEFAULT_TOKENIZER_CONTEXT_TOKENS,
    )

    if not local_files_only:
        logger.warning(
            "Loading tokenizer '%s' from Hugging Face Hub with trust_remote_code=True. "
            "Set TOKENIZER_MODEL_PATH or LM_STUDIO_MODELS_DIR to use a local copy.",
            resolution.model,
        )

    # trust_remote_code is required for models such as Qwen3 whose
    # tokenizer_config.json references a custom tokenizer class. The operator
    # controls which model is used via env vars, so the trust boundary is
    # equivalent to a pip-install from that source. When local_files_only=True
    # the code runs from files already on disk — the operator owns that risk.
    _tokenizer_cache = HuggingFaceTokenizer.from_pretrained(
        model_name=resolution.model,
        max_tokens=max_tokens or DEFAULT_TOKENIZER_CONTEXT_TOKENS,
        trust_remote_code=True,
        local_files_only=local_files_only,
    )
    logger.info(
        "PDF chunker tokenizer loaded tokenizer_class=%s source=%s model=%s",
        type(_tokenizer_cache).__name__,
        resolution.source,
        resolution.model,
    )
    return _tokenizer_cache


def _reset_tokenizer_cache() -> None:
    """Reset the module-level tokenizer cache. For use in tests only."""
    global _tokenizer_cache
    _tokenizer_cache = None
