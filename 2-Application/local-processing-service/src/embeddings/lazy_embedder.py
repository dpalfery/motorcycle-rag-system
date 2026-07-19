"""Lazy proxy that defers embedder discovery and construction until first use."""

from __future__ import annotations

import logging
import threading
from collections.abc import Callable

from .embedder import Embedder

logger = logging.getLogger(__name__)


class LazyEmbedder(Embedder):
    """Defers network discovery and concrete embedder construction.

    Construction of this proxy is side-effect free: no network I/O occurs
    until the first call to :meth:`check_status`, :meth:`generate_embedding`,
    or :meth:`generate_embeddings_batch`.

    Configured endpoint/model attributes are exposed immediately so
    ``/health`` can report them before the inner embedder is ready.
    Init/discovery failures never raise from :meth:`check_status` (returns
    ``'disconnected'``); embed methods raise :class:`RuntimeError` so jobs
    fail clearly while the process stays up.
    """

    def __init__(
        self,
        factory: Callable[[], Embedder],
        *,
        endpoint: str | None = None,
        host: str | None = None,
        base_url: str | None = None,
        model: str | None = None,
    ) -> None:
        """Create a lazy embedder proxy.

        Args:
            factory: Zero-arg callable that performs discovery and returns a
                fully configured :class:`Embedder` (typically a
                :class:`TruncatingEmbedder` wrapping a concrete provider).
            endpoint: Configured provider endpoint for health reporting.
            host: Configured Ollama-style host for health reporting.
            base_url: Alternate base URL attribute for health reporting.
            model: Configured model name for health reporting.
        """
        self._factory = factory
        self._endpoint = endpoint
        self._host = host
        self._base_url = base_url
        self._model = model
        self._resolved: Embedder | None = None
        self._init_error: BaseException | None = None
        self._lock = threading.Lock()

    @property
    def _embedder(self) -> Embedder:
        """Health unwrap target matching :class:`TruncatingEmbedder` pattern.

        When the inner embedder is ready, returns the concrete provider
        (unwrapping :class:`TruncatingEmbedder` if present). Otherwise
        returns ``self`` so configured ``_endpoint`` / ``_host`` / ``_model``
        remain visible to ``_build_health_response``.
        """
        if self._resolved is not None:
            return getattr(self._resolved, "_embedder", self._resolved)
        return self

    def _try_resolve(self) -> Embedder | None:
        """Attempt discovery/construction; return ``None`` on failure."""
        if self._resolved is not None:
            return self._resolved

        with self._lock:
            if self._resolved is not None:
                return self._resolved

            try:
                resolved = self._factory()
            except Exception as exc:
                previous = self._init_error
                self._init_error = exc
                # Retry on later health/embed calls so a late-started provider
                # can recover; only log when the failure is new or changes.
                if (
                    previous is None
                    or type(previous) is not type(exc)
                    or str(previous) != str(exc)
                ):
                    logger.warning(
                        "Embedding provider initialisation failed: %s",
                        exc,
                    )
                return None

            concrete = getattr(resolved, "_embedder", resolved)
            endpoint = getattr(concrete, "_endpoint", None)
            host = getattr(concrete, "_host", None)
            base_url = getattr(concrete, "_base_url", None)
            model = getattr(concrete, "_model", None)
            if endpoint:
                self._endpoint = endpoint
            if host:
                self._host = host
            if base_url:
                self._base_url = base_url
            if model:
                self._model = model

            self._resolved = resolved
            self._init_error = None
            return self._resolved

    def _require_resolved(self) -> Embedder:
        """Return the resolved embedder or raise a clear runtime error."""
        resolved = self._try_resolve()
        if resolved is not None:
            return resolved

        detail = self._init_error or "unknown initialisation error"
        raise RuntimeError(
            f"Embedding provider failed to initialise: {detail}"
        ) from self._init_error

    async def generate_embedding(self, text: str) -> list[float]:
        """Return a vector embedding for *text* via the resolved provider.

        Raises:
            RuntimeError: If discovery/construction has failed.
        """
        resolved = self._require_resolved()
        return await resolved.generate_embedding(text)

    async def generate_embeddings_batch(self, texts: list[str]) -> list[list[float]]:
        """Return embeddings for a batch of texts via the resolved provider.

        Raises:
            RuntimeError: If discovery/construction has failed.
        """
        resolved = self._require_resolved()
        return await resolved.generate_embeddings_batch(texts)

    async def check_status(self) -> str:
        """Return provider health; never raises on init/discovery failure.

        Returns:
            ``'connected'`` / ``'disconnected'`` from the inner embedder,
            or ``'disconnected'`` when initialisation has not succeeded.
        """
        resolved = self._try_resolve()
        if resolved is None:
            return "disconnected"
        return await resolved.check_status()
