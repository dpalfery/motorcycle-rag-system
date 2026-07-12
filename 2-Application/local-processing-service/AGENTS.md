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

Run tests through the project-local environment, for example `.venv/bin/python -m pytest`.
