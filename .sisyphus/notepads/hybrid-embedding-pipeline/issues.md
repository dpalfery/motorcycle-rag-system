# Issues — hybrid-embedding-pipeline

## 2026-03-01 — Pre-existing LSP errors in .NET Persistence project
Files affected: `AzureOpenAIClientWrapper.cs`, `AzureSearchClientWrapper.cs`, `MotorcycleIndexingService.cs`, `AzureSearchDocumentService.cs`
LSP reports "Azure namespace not found", "ILogger not found" etc.
**These are pre-existing** — not caused by our changes. Likely NuGet packages not restored in LSP session.
**Resolution**: Ignore LSP. Use `dotnet build` as the authoritative pass/fail check.

## 2026-03-01 — Pre-existing Python errors in main.py
File: `src/main.py` lines 115 and 149 — arguments missing for status update function.
These are pre-existing from the local-docling-ingestion plan and are not related to this plan.
Do NOT fix these as part of this plan — they are out of scope.
