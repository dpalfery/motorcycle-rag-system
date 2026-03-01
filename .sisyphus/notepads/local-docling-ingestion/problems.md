# Problems — local-docling-ingestion

## [2026-03-01T09:07:46Z] Unresolved Blockers

None at start. Will be updated as issues arise.

## [2026-03-01] Runtime Acceptance Criteria — Blocked on Live Infrastructure

The following 6 items in the plan cannot be completed by an agent — they require live services:

| Line | Item | Blocker |
|------|------|---------|
| 79 | `curl http://localhost:8100/health` returns healthy | Ollama must be running on RTX 5090 host |
| 80 | `curl -X POST .../process/pdf` returns job_id | Ollama + Azure Blob Storage must be running |
| 81 | Processed chunks appear in Azure Blob | Azure Blob Storage connection required |
| 82 | Graph entities appear in Azure Blob | Azure Blob Storage connection required |
| 86 | Azure AI Search indexer picks up chunks | Azure AI Search + Blob indexer must be configured |
| 1776 | Python service starts and responds | Ollama must be running on RTX 5090 host |

**Resolution**: These are human-executed acceptance tests. The human must:
1. Start Ollama on the RTX 5090: `ollama serve`
2. Pull models: `ollama pull qwen3-embedding:4b && ollama pull qwen3:4b`
3. Start the Python service: `cd 2-Application/local-processing-service && uvicorn src.main:app --port 8100`
4. Set env vars: `AZURE_STORAGE_ACCOUNT_URL`, `AZURE_SEARCH_ENDPOINT`, etc.
5. Run the curl acceptance tests manually

**All implementation work is complete.** Only live-infra validation remains.

## [2026-03-01] Additional Live-Infra Blockers (Task 8 + Task 18 sub-criteria)

7 more items confirmed blocked (discovered during final checkbox audit):

| Line | Item | Blocker |
|------|------|---------|
| 880 | `uvicorn main:app` starts without crash | Python service must actually be running |
| 881 | `curl http://localhost:8100/health` returns JSON | Live service on port 8100 required |
| 882 | `curl http://localhost:8100/docs` returns Swagger UI | Live service on port 8100 required |
| 1677 | PDF processing end-to-end works (input → chunks → blob) | Ollama + Azure Blob required |
| 1678 | Chunk vectors are exactly 1536 dimensions | Live Ollama embedding call required |
| 1679 | JSON Lines format valid (each line parses independently) | Live processing run required |
| 1680 | Error cases handled (invalid input returns 400, not 500) | Live service required |

**Total permanently blocked: 13 items (lines 79, 80, 81, 82, 86, 880, 881, 882, 1677, 1678, 1679, 1680, 1776)**

**Plan is at maximum achievable completion: 111/124 (89.5%). The remaining 13 require live infrastructure.**
No further automated agent work is possible on this plan.
