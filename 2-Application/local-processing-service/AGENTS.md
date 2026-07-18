# Local Processing Service Instructions

## Applies to

`2-Application/local-processing-service/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Processor onboarding](../../6-Docs/local-processing-service/onboarding.md)
- [Processor architecture](../../6-Docs/local-processing-service/architecture.md)
- [Processor requirements](../../6-Docs/local-processing-service/requirements.md)
- [Current local-processing plan](../../6-Docs/archive/plans/2026-07-07-chunk-upload-fix-and-serverless-search.md) for ingestion/indexing changes

## Scoped constraints

- The service owns Python ingestion, chunking, embedding-provider integration, and local processor workflow.
- Use the project-local virtual environment and current configuration/source as the authority for provider selection, endpoint defaults, and embedding dimensions. Do not treat historical `.env.example` comments as runtime truth.
- Keep local-first ingestion boundaries intact: Admin Desktop controls the watch-folder and runtime process; cloud uploads use the approved API contract.

## Verify

The test suite lives under [`5-Test/local-processing-service.Tests/`](../../5-Test/local-processing-service.Tests/AGENTS.md). From this directory run:

```bash
.venv/bin/python -m pytest -c pyproject.toml --rootdir=. ../../5-Test/local-processing-service.Tests --ignore=../../5-Test/local-processing-service.Tests/integration
```

Pass `-c` / `--rootdir` so pytest keeps this package's `pyproject.toml` settings (`pythonpath`, asyncio defaults). Do not rely on `testpaths` pointing outside this directory — pytest would re-root to the repository and drop the config.
