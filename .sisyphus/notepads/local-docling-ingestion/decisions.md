# Decisions — local-docling-ingestion

## [2026-03-01T09:07:46Z] Architecture Decisions

### Worktree
- Path: `D:/motorcycle-rag-system-local-docling` (detached HEAD from develop branch)
- All work happens in this worktree

### Task Execution Order
- Wave 1 (7 parallel): Tasks 1-7 (Python scaffold + Python modules + ILocalPipelineService)
- Wave 2 (4 tasks): Tasks 8-11 (wire Python + LocalPipelineService.cs + rename options + indexer script)
- Wave 3 (4 tasks): Tasks 12-15 (IngestionJobService update + GraphEntityIngestionService + DI + Obsolete)
- Wave 4 (3 tasks): Tasks 16-18 (pytest + xunit + integration test)
- Final (4 tasks): F1-F4 (reviews)

### ProcessingMode Default
- Default: `ProcessingMode.Local` (not Fabric) — new installations get local by default
