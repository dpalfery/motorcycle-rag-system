# Local Processing Service Tests Instructions

## Applies to

`5-Test/local-processing-service.Tests/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Processor onboarding](../../6-Docs/local-processing-service/onboarding.md)
- [Processor architecture](../../6-Docs/local-processing-service/architecture.md)
- [Processor requirements](../../6-Docs/local-processing-service/requirements.md)

## Scoped constraints

- Keep tests deterministic and do not log tokens, embeddings content, or other PII.
- Respect local-first ingestion boundaries: do not assume network access beyond the configured local embedding provider, and keep watch-folder/path assumptions aligned with the Admin Desktop-managed boundary described in the processor docs.
- These tests import their subjects from `src/` via `pythonpath = ["src"]` in `2-Application/local-processing-service/pyproject.toml`. Do not add `__init__.py` files here — the suite relies on `prepend`-mode basename collection to avoid colliding with the source `embeddings` package.

## Verify

Run `.venv/bin/python -m pytest` from `2-Application/local-processing-service/`. The suite now lives here, but `testpaths` in the service's `pyproject.toml` points at this directory, so pytest still discovers and runs it with the service's rootdir, `pythonpath`, and async settings applied.
