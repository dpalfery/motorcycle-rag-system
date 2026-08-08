---
id: plans/2026-08-03-processor-artifact-skip-observability
title: Processor artifact search-chunk skip observability and orphan reconciliation
doc-type: plan
status: archived
component: MotorcycleRAG system
owner: Maintainers
last-reviewed: 2026-08-04
code-refs:
  - ProcessorArtifactService
  - SearchChunkIndexingCoordinator
  - OrphanedArtifactSweepService
  - OrphanedArtifactSweepBackgroundService
  - IngestionJobsController
api-endpoints:
  - GET /api/ingestion/artifacts/orphaned
  - POST /api/ingestion/artifacts/orphaned/sweep
  - POST /api/ingestion/artifacts/orphaned/{uploadId}/adopt
decided-by: []
supersedes: []
---
# Processor artifact search-chunk skip observability and orphan reconciliation

**Status:** Archived
**Date:** 2026-08-03
**Archived:** 2026-08-04
**Goal:** Make the fail-closed "no ingestion job → skip chunk indexing" path in `ProcessorArtifactService` observable, consistently logged, and durably marked on the blob; then build the reconciliation sweep that re-drives those orphans to indexed success or to a terminal state a human can see and resolve — without ever reversing fail-closed.

---

## 1. Problem / Motivation

`ProcessorArtifactService.ProcessSearchChunksAsync` returns early when `FindLatestSearchChunkJobAsync` finds no ingestion job for the upload. That early return is **correct**: `IChunkIndexingService.IndexFromJsonlAsync` is a single 5-parameter overload precisely because passing `Guid.Empty`/`string.Empty` anchors made Azure AI Search `mergeOrUpload` treat those fields as "clear this field", silently wiping real anchors off already-indexed documents (documented at `3-Domain/MotorcycleRAG.Contracts/Interfaces/IChunkIndexingService.cs` lines 15-23). Indexing anyway is not an option. **Fail-closed stays.**

What is defective is everything *around* the skip:

1. **The skip is invisible.** `ProcessSearchChunksAsync` returns `Task` (void). `UploadArtifactAsync` unconditionally returns `ProcessorArtifactOperationStatus.Success` with `Response.Status = "stored"`. The Python local processor receives HTTP 202 and logs "Uploaded search-chunks artifact" whether the chunks reached Azure AI Search or not.
2. **Inconsistent severity.** `job is null` logs `LogWarning`; the two `Guid.Empty` guards log `LogError` with "This is a bug." All three mean "the anchor contract cannot be satisfied, chunks are being dropped."
3. **No orphan marker and no way back.** The happy path stamps blob metadata `state` + `dateLastProcessed`. The skip path stamps nothing. A "stored but never indexed" blob is indistinguishable from an unprocessed one, cannot be enumerated, and there is no mechanism that ever retries it. **The manual was chunked and paid for, and the content is silently absent from search forever.**
4. **Dead defensive check.** `indexedArtifactId == Guid.Empty` is evaluated immediately after `indexedArtifactId = Guid.NewGuid()`, with no reassignment in between.
5. **Unpinned.** No test asserts anything about the skip path; the one existing no-job test actively asserts the *absence* of a marker.

## 2. Approved decisions

| ID | Decision |
|----|----------|
| D1 | Fail-closed stays. The skip never becomes a fallback to indexing with synthetic anchors, in the upload path or the sweep. Every indexing call carries a real `indexedArtifactId`, a real `ingestionJobId`, and a `sourceContentHash` that is a real value or `null` — never `Guid.Empty`/`string.Empty`. |
| D2 | The dead `indexedArtifactId == Guid.Empty` check (lines 196-200) is genuinely unreachable and is deleted, not replaced. Verified: assigned from `Guid.NewGuid()` at line 186, not reassigned before the check. The adjacent `job.IngestionJobId == Guid.Empty` check (line 202) is **retained** — that value is externally sourced. No third anchor is a `Guid` (`sourceContentHash` is `string?`), so no "intended but mis-written" check is missing. |
| D3 | The existing test `UploadArtifactAsync_SearchChunksArtifact_NoJobFound_SkipsCatalogWriteAndSkipsBlobMetadata` (`ProcessorArtifactServiceTests.cs:335`) is amended in place, not deleted: catalog/job `Times.Never` assertions are kept, the `SetMetadataAsync` `Times.Never` assertion is inverted to assert the orphan stamp, and it is renamed. The stale `// D3: ... SetMetadataAsync is not invoked` comment at line 340 is corrected. |
| D4 | No orphan row is written to `IndexedArtifacts`. Verified impossible: `schema.sql:819,833` declares `IngestionJobId UNIQUEIDENTIFIER NOT NULL` + `FK_IndexedArtifacts_IngestionJobs`. The absent job is exactly what makes a row unwritable. **Blob metadata is the orphan state machine.** |
| D5 | The orphan marker is an Application-layer constant set — `state=Orphaned` plus `orphanReason=NoIngestionJob` — written into the existing blob-metadata schema. The `IndexedArtifactState` domain enum is **not** extended; per D4 no catalog row could ever hold such a value. |
| D6 | All anchor-contract-unsatisfiable branches log at `LogError`, each with a stable `EventId` so an alert rule can bind to it. `Warning` under-reports silent data loss; `Critical` stays reserved for host-threatening conditions. |
| D7 | Orphan enumerability *and* the reconciliation sweep are in scope: `BlobObjectDescriptor`/`ListAsync` carry metadata, a sweep re-drives orphans on a cadence, orphans reach a terminal state, and an admin surface exposes them. Delivered as Phases 2-4 below. |
| D8 | The sweep's only success path is "an ingestion job now exists." Because of D1 the sweep **cannot** manufacture anchors, so it can heal the *race* (job committed after the artifact upload landed) but not the *permanent* case (a job that will never exist). The permanent case is deliberately routed to a terminal state plus a human resolution surface rather than an infinite retry. |
| D9 | Retry is bounded by **both** an attempt budget and a wall-clock retention window, whichever trips first, so a permanently jobless artifact cannot retry forever. Defaults: `MaxOrphanRetryAttempts = 5`, `OrphanRetentionWindow = 24h`, `OrphanSweepInterval = 5min`, all overridable via `IngestionOptions`. |
| D10 | Terminal orphans are **never auto-deleted**. The blob is the only surviving copy of paid-for chunking work; deletion is an explicit operator action. |
| D11 | The sweep is driven by a `BackgroundService` following the established `JobDeletionBackgroundService` shape (singleton, `IServiceScopeFactory` per cycle, never-die loop, `SemaphoreSlim(1,1)` serialization) **and** is additionally triggerable on demand from the admin surface. |
| D12 | The post-job-resolution indexing body is extracted from `ProcessSearchChunksAsync` into a single shared collaborator so the upload path, the sweep, and the adopt action all honor the anchor contract in exactly one place. Behavior on the upload path is unchanged; this is a pure extraction. |
| D13 | Scope is confined to findings 1-5 plus the D7 reconciliation work. No unrelated refactoring (e.g. residual per-call `LogSanitizer.Sanitize` sites superseded by the central `SanitizingLoggerProvider`). |
| D14 | The indexing skip is surfaced as a new `ProcessorArtifactOperationStatus.IndexingSkipped` with `ProcessorArtifactUploadResponse.Status = "stored-not-indexed"`, and the controller maps it to **202 Accepted** — the same HTTP code as today. 202 stays truthful because the artifact genuinely was accepted and stored; the Python client is behaviorally unaffected; the durable signal is the blob marker plus the `LogError` EventId, with the response status as the immediate synchronous acknowledgement. A 4xx/5xx mapping is rejected: it would misreport storage that succeeded, and would either trigger pointless client re-uploads (5xx) or fail a processor run for a condition the sweep usually heals within one poll interval (4xx). |
| D15 | The admin surface is read-only listing **plus** an adopt action: `POST /api/ingestion/artifacts/orphaned/{uploadId}/adopt` binds an orphaned blob to an operator-supplied `ingestionJobId` and indexes it through the shared coordinator. Adopt is safe under D1 because the operator supplies a real job id, so no synthetic anchor is ever written. It is required because the terminal class (D8) is exactly the class the sweep cannot heal — listing without adopt would show an operator a stuck artifact with no in-product resolution. **No discard/delete action** is included: destroying the only surviving copy of paid-for chunking work is a materially different risk that deserves its own decision once the orphan population is understood. |

## 2a. Open questions (decision ledger)

All questions are resolved. No `OPEN` rows remain; deliberately deferred items live in §7.

| Q# | Question | Recommended | Status |
|----|----------|-------------|--------|
| Q1 | How is the indexing skip surfaced to the caller of `UploadArtifactAsync`? | (a) `IndexingSkipped` + `"stored-not-indexed"` + 202 | ANSWERED: (a) → D14 |
| Q2 | What exact value is stamped as the blob orphan marker? | (b) Application-layer constant | ANSWERED: (b) `state=Orphaned` + `orphanReason=NoIngestionJob`, domain enum untouched → D5 |
| Q3 | Is making the orphan marker enumerable in scope? | (a) out of scope | ANSWERED: (c) widest — enumerability **and** the reconciliation sweep in scope → D7 |
| Q4 | What log severity for the anchor-unsatisfiable branches? | (a) all `LogError` | ANSWERED: (a) → D6 |
| Q5 | Does the admin surface include an adopt action? | (a) include adopt | ANSWERED: (a) listing + adopt, no discard/delete → D15 |

## 3. Investigation findings

CodeGraph MCP was **not available** to the planning agent (no `codegraph_explore` tool exposed); findings were gathered by targeted `Read`/`Grep` and that fallback is disclosed per repository `AGENTS.md`. Line numbers were verified against the working tree on 2026-08-03; re-verify before editing.

### The defect surface

- **`ProcessSearchChunksAsync`** — `ProcessorArtifactService.cs:178-275`, returns `Task`. Skip at 189-193 (`LogWarning`), dead guard at 196-200, live guard at 202-206, happy-path metadata stamp at 252-267 writing `["state"] = artifactState.ToString()` and `["dateLastProcessed"]`.
- **`UploadArtifactAsync`** — lines 93-151; invokes `ProcessSearchChunksAsync` fire-and-discard at 139, then constructs an unconditional `Status = "stored"` / `Success` at 142-150.
- **Result contract** — `ProcessorArtifactUploadResult` + `ProcessorArtifactOperationStatus` in `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/ProcessorArtifactUseCaseModels.cs` (members: `Success`, `InvalidUploadId`, `InvalidDocumentType`, `InvalidArtifactType`, `Unauthorized`, `NotFound`). `ProcessorArtifactUploadResponse.Status` defaults to `"stored"`.
- **HTTP adapter** — `ProcessorArtifactsController.cs:85-91`. `Success => Accepted(...)`; **the discard arm `_ =>` maps to 500.** A new enum member without a matching controller arm silently becomes a 500. The controller change is mandatory, not cosmetic.
- **Finding 4 confirmed dead** — no reassignment of `indexedArtifactId` between lines 186 and 196.

### Client and blast-radius facts that constrain the design

- **Python client ignores the response body.** `2-Application/local-processing-service/src/api/api_client.py:195-265`: `upload_processed_artifact` returns `None`, treats `is_success or status_code == 202` as success, **retries 5xx** with exponential backoff, and raises on non-retryable <500 non-success. So: a body-only signal is invisible to it; a 5xx would trigger pointless re-uploads of an already-stored artifact; a 4xx would make the processor treat successful storage as hard failure. This is the evidence behind D14.
- **Orphan catalog row impossible** — `schema.sql:817-838` (D4).
- **`IndexedArtifactState` persists as a string** — `IndexedArtifactRepository.cs:292` uses `Enum.Parse<IndexedArtifactState>(row.State)`; column is `NVARCHAR(50)` with no CHECK constraint. Appending a member would be *schema*-safe, but per D4 no row could carry it — hence D5.
- **Nothing reads the `state` blob metadata key today.** Exactly one writer (`ProcessorArtifactService.cs:259`), zero readers.
- **`ListAsync` discards metadata** — `AzureBlobStorageService.cs:100-127` calls `GetBlobsAsync` without `BlobTraits.Metadata`; `BlobObjectDescriptor` exposes only `Name`, `ContentType`, `SizeBytes`, `LastModifiedUtc`. This is the enumerability gap D7 closes.
- **`SetMetadataAsync` replaces the entire metadata collection** (Azure Set Blob Metadata semantics) and is doubly best-effort: the Persistence implementation swallows and logs (`AzureBlobStorageService.cs:181-199`, pinned by `SetMetadataAsync_WhenSdkFails_SwallowsFailureAndLogsWarning`), and `ProcessSearchChunksAsync` wraps it again. **Implementation trap:** every write must supply the full intended key set; a partial write silently drops orphan keys. On successful re-drive that is the desired effect (the happy-path stamp naturally clears the orphan keys); on an attempt-increment it would be a bug.
- **Blob metadata key names must be valid C# identifiers and are case-insensitive.** `state`, `dateLastProcessed`, `orphanReason`, `orphanAttempts`, `orphanFirstDetectedUtc` all comply.
- **`SetMetadataAsync` passes no `BlobRequestConditions`**, so concurrent writers are last-write-wins (residual R1).
- **Container name is a literal, not config** — `TryGetArtifactLocation` (`ProcessorArtifactService.cs:337-341`) hardcodes `"search-chunks"` and path `{uploadId}/chunks.jsonl`. The sweep must reuse the same constant/helper rather than introducing a parallel option.

### Reusable precedent (do not reinvent)

- **`ChunkReprocessService`** (`2-Application/.../Ingestion/ChunkReprocessService.cs`) already does bounded, idempotent, continue-on-error re-drive — but `ReprocessByJobIdAsync` starts from `GetByUploadAndTypeAsync` and returns a no-op when no artifact row exists (lines 66-69). **Orphans have no row, so it cannot be reused as-is** — that is precisely the gap this plan fills.
- **`JobDeletionBackgroundService`** (`2-Application/.../Ingestion/JobDeletionBackgroundService.cs:36-112`) is the canonical hosted-service shape: const poll interval, `IServiceScopeFactory` per cycle, `SemaphoreSlim(1,1)`, never-die try/catch, graceful `OperationCanceledException` exit. `GraphIngestionBackgroundServiceTests` shows the reflection-driven `ExecuteAsync` test technique.
- **Admin surface** — `IngestionJobsController` is `[Route("api/ingestion")]` + `[Authorize(Policy = "mcr-api-admin")]` + `[EnableRateLimiting("ingestion-jobs")]` and already injects `IChunkReprocessService`. The orphan endpoints belong here, not on `ProcessorArtifactsController` (which is `mcr-api-local-processor`, machine-to-machine).
- **DI registration** — `1-Presentation/MotorcycleRAG.API/Configuration/Services/DataPipelineConfiguration.cs:46,50,55`.

### End-to-end resolution path for an orphaned artifact

**Step 1 — Detection (upload time).** Job resolution fails → chunks are stored but not indexed → `UploadArtifactAsync` returns `IndexingSkipped` / `"stored-not-indexed"` (HTTP 202, D14) and the blob is stamped `state=Orphaned`, `orphanReason=NoIngestionJob`, `orphanAttempts=0`, `orphanFirstDetectedUtc=<now>`, `dateLastProcessed=<now>`.

**Step 2 — Notification at that moment.** Three channels, so nothing reports silent success:
- a `LogError` with a stable `EventId` (D6) — the hook an Azure Monitor alert rule binds to;
- the HTTP response status the Python processor receives, which the processor logs as a warning (T5);
- the durable blob marker, which makes the condition survive a process restart and become queryable.

**Step 3 — Automatic re-drive.** `OrphanedArtifactSweepBackgroundService` polls every `OrphanSweepInterval` (default 5 min). Each cycle: list the `search-chunks` container **with metadata**, select blobs where `state == Orphaned` (terminal excluded), parse `uploadId` from the blob path, and re-run job resolution. If a job now exists → download the blob and run the shared indexing coordinator with real anchors → the artifact indexes, catalog and chunk rows are written, the job transitions terminal, and the happy-path metadata stamp overwrites the orphan keys. The orphan is healed with no human involvement.

**Step 4 — Bounded retry and terminal failure.** If no job is found, `orphanAttempts` is incremented and the full key set re-stamped. When `orphanAttempts >= MaxOrphanRetryAttempts` **or** `now - orphanFirstDetectedUtc >= OrphanRetentionWindow` (D9), the blob is stamped `state=OrphanedTerminal` with `orphanReason=NoIngestionJobAfterMaxAttempts` or `NoIngestionJobWithinRetentionWindow`, and a `LogError` with its own stable `EventId` fires. Terminal blobs are excluded from subsequent sweeps — that is what stops infinite retry when the root cause is permanent (D8). Nothing is deleted (D10).

**Step 5 — Human visibility and resolution.** `GET /api/ingestion/artifacts/orphaned` (admin) returns every `Orphaned` and `OrphanedTerminal` artifact with its reason, attempt count, first-detected timestamp and blob path — the answer to "which N artifacts are stuck." From there the operator either repairs/creates the ingestion job and lets the next sweep heal it, triggers `POST /api/ingestion/artifacts/orphaned/sweep` for an immediate pass, or adopts the artifact onto an explicit `ingestionJobId` (D15). Authoring the Azure Monitor alert rule against the two `EventId`s is Azure infrastructure that ships through Pulumi CI/CD and is recorded as an infra follow-up, not an agent action.

**Idempotency and concurrency.** Job resolution is a pure read. The indexing core reuses the same idempotent upsert sequence `ChunkReprocessService` already depends on (artifact upsert keyed by the `UQ_IndexedArtifacts_Upload_Type` unique index, `DeleteByArtifactIdAsync` then `UpsertManyAsync`), and `TryTransitionSearchChunkJobToTerminalAsync` only transitions from `Indexing`/`Processing`/`Queued`, so an already-terminal job is not re-transitioned. `state` transitions are monotonic toward terminal. Under multi-instance hosting, last-write-wins metadata could double-count `orphanAttempts`; that degrades safely (terminal is reached sooner, and re-indexing is idempotent) and is recorded as residual R1.

## 4. Test contract

The RED tests below define done-ness for each implementation task and are authored before it. A task is done only when its contract tests pass.

| Task # | Test project / file | Runner command | Behavior asserted (RED → GREEN) |
|--------|---------------------|----------------|---------------------------------|
| T3 | `5-Test/MotorcycleRAG.Application.Tests/Services/Ingestion/ProcessorArtifactServiceTests.cs` | `rtk dotnet test 5-Test/MotorcycleRAG.Application.Tests/MotorcycleRAG.Application.Tests.csproj --filter FullyQualifiedName~ProcessorArtifactServiceTests` | search-chunks upload with no ingestion job → result `Status` is `IndexingSkipped` and `Response.Status` is `"stored-not-indexed"`; indexing is never invoked; catalog/job writes never occur; graph-entities and happy-path uploads still return `Success`/`"stored"` |
| T3 | same file | same | both anchor-unsatisfiable branches (`job is null`, `job.IngestionJobId == Guid.Empty`) log at `Error` with their stable `EventId`; no `Warning`-level record is emitted for the skip |
| T3 | same file | same | regression: happy path, partial-indexing path, indexing-throws path and metadata-throws path retain existing outcomes (`Success`, computed `IndexedArtifactState` stamp, job transitions, no exception escapes) |
| T4 | `5-Test/MotorcycleRAG.API.Tests/Controllers/ProcessorArtifactsControllerTests.cs` | `rtk dotnet test 5-Test/MotorcycleRAG.API.Tests/MotorcycleRAG.API.Tests.csproj --filter FullyQualifiedName~ProcessorArtifactsControllerTests` | `IndexingSkipped` maps to 202 Accepted carrying the response body, and **not** to the `_ =>` 500 arm; existing 400/500 mappings unchanged |
| T5 | `5-Test/local-processing-service.Tests/api/test_api_client.py` | `rtk pytest 5-Test/local-processing-service.Tests -k api_client` | a 202 whose body `status` is not `"stored"` is logged at warning level with the uploadId and returned status; the upload still succeeds (no raise, no retry) |
| T7 | `5-Test/MotorcycleRAG.Application.Tests/Services/Ingestion/ProcessorArtifactServiceTests.cs` | as T3 | no-job skip calls `SetMetadataAsync` exactly once on the artifact's own container/path with the **complete** key set `state=Orphaned`, `orphanReason=NoIngestionJob`, `orphanAttempts=0`, `orphanFirstDetectedUtc`, `dateLastProcessed`; a throwing `SetMetadataAsync` still does not throw out of `UploadArtifactAsync` and does not change the returned status (amends the D3 test) |
| T9 | `5-Test/MotorcycleRAG.Persistence.Tests/Azure/AzureBlobStorageServiceTests.cs` | `rtk dotnet test 5-Test/MotorcycleRAG.Persistence.Tests/MotorcycleRAG.Persistence.Tests.csproj --filter FullyQualifiedName~AzureBlobStorageServiceTests` | `ListAsync` requests metadata and projects it onto `BlobObjectDescriptor.Metadata` (empty, never null, when the blob has none); `GetMetadataAsync` returns the blob's metadata and an empty map for a missing blob; existing `ListAsync`/`SetMetadataAsync` behavior unchanged |
| T11 | `5-Test/MotorcycleRAG.Application.Tests/Services/Ingestion/SearchChunkIndexingCoordinatorTests.cs` (new) | as T3 | given real anchors and a JSONL stream: calls the sole 5-parameter `IndexFromJsonlAsync` with the resolved `indexedArtifactId`/`ingestionJobId`/`sourceContentHash`, never `Guid.Empty`/`string.Empty`; upserts the artifact with the computed `IndexedArtifactState`; replaces chunk rows; transitions the job; stamps the happy-path metadata set (which clears orphan keys) |
| T11 | `.../ProcessorArtifactServiceTests.cs` | as T3 | extraction is behavior-preserving: every pre-existing upload-path assertion still holds with the coordinator in place |
| T13 | `5-Test/MotorcycleRAG.Application.Tests/Services/Ingestion/OrphanedArtifactSweepServiceTests.cs` (new) | as T3 | (a) `state=Orphaned` blob whose job now exists → coordinator invoked with that job's id, orphan healed; (b) job still absent → `orphanAttempts` incremented, full key set rewritten, `orphanFirstDetectedUtc` preserved; (c) attempts reach `MaxOrphanRetryAttempts` → `state=OrphanedTerminal` + `orphanReason=NoIngestionJobAfterMaxAttempts` + `Error` log with stable EventId; (d) `orphanFirstDetectedUtc` older than `OrphanRetentionWindow` → terminal with `NoIngestionJobWithinRetentionWindow` even below the attempt budget; (e) `OrphanedTerminal` blobs are skipped entirely; (f) non-orphan `state` values are ignored; (g) one blob throwing does not abort the cycle and the result counts reflect it; (h) the blob is never deleted in any branch |
| T15 | `5-Test/MotorcycleRAG.Application.Tests/Services/Ingestion/OrphanedArtifactSweepBackgroundServiceTests.cs` (new) | as T3 | `ExecuteAsync` (reflection-invoked, per the `GraphIngestionBackgroundServiceTests` precedent) runs a cycle per interval, resolves the sweep service from a fresh DI scope each cycle, survives a throwing cycle and continues, and exits gracefully on cancellation without throwing |
| T17 | `5-Test/MotorcycleRAG.API.Tests/Controllers/IngestionJobsControllerTests.cs` | `rtk dotnet test 5-Test/MotorcycleRAG.API.Tests/MotorcycleRAG.API.Tests.csproj --filter FullyQualifiedName~IngestionJobsControllerTests` | `GET artifacts/orphaned` returns 200 with both `Orphaned` and `OrphanedTerminal` entries including reason/attempts/first-detected; `POST artifacts/orphaned/sweep` returns 202 with the sweep counts; `POST artifacts/orphaned/{uploadId}/adopt` returns 202 for a valid job id and drives the coordinator with that id, 404 for an unknown orphan, 400 for a malformed or absent job id; no endpoint accepts an empty/`Guid.Empty` job id; all three require `mcr-api-admin` |
| T2 (D2) | — | — | `no-test`: deleting unreachable code has no observable behavior to assert. Validation is the compiler plus the T3 regression rows staying green. |
| T18 | — | — | `no-test`: documentation closeout. Validation is `docs-dev` verifying acceptance criteria against implementation evidence and updating the canonical docs and plan index. |

## 5. Task list

| # | Phase | Component | Description | Skills | Test contract |
|---|-------|-----------|-------------|--------|---------------|
| T1 | 1 | Application/API tests | Author the RED tests for the T3/T4 rows in §4 (skip status, severity + EventIds, controller mapping, regressions). | test-authoring, xunit, moq | authors §4 T3, T4 |
| T2 | 1 | `ProcessorArtifactService.cs` | Delete the unreachable `indexedArtifactId == Guid.Empty` block (D2); keep the `job.IngestionJobId` guard. | dotnet, clean-architecture | §4 T2 — `no-test` |
| T3 | 1 | `ProcessorArtifactService.cs`, `ProcessorArtifactUseCaseModels.cs`, `ProcessorArtifactUploadResponse.cs` | Change `ProcessSearchChunksAsync` to return an outcome the caller inspects; add `ProcessorArtifactOperationStatus.IndexingSkipped` and the `"stored-not-indexed"` body status (D14); raise both branches to `LogError` with stable `EventId`s (D6). | dotnet, clean-architecture, logging | §4 T3 |
| T4 | 1 | `ProcessorArtifactsController.cs` | Add the explicit `IndexingSkipped => Accepted(result.Response)` arm so the new status cannot fall into the `_ =>` 500 arm. | dotnet, aspnetcore | §4 T4 |
| T5 | 1 | `2-Application/local-processing-service/src/api/api_client.py` | Log a warning when a 202 response body's `status` is not `"stored"`; do not raise and do not retry. | python, pytest | §4 T5 |
| T6 | 2 | Application tests | Author the RED T7 orphan-stamp tests; amend the D3 test in place (rename + invert the `SetMetadataAsync` assertion + fix the stale comment). | test-authoring, xunit, moq | authors §4 T7 |
| T7 | 2 | `ProcessorArtifactService.cs` (+ orphan metadata constants) | Stamp the complete orphan key set on the skip path inside the existing best-effort try/catch (D5); introduce the shared metadata key/value constants and the shared `search-chunks` container/path constant. | dotnet, azure-storage | §4 T7 |
| T8 | 2 | Persistence tests | Author the RED T9 tests for metadata-carrying `ListAsync` and the new `GetMetadataAsync`. | test-authoring, xunit, moq | authors §4 T9 |
| T9 | 2 | `IBlobStorageService.cs`, `BlobObjectDescriptor.cs`, `AzureBlobStorageService.cs` | Add `Metadata` to `BlobObjectDescriptor` (empty, never null); pass `BlobTraits.Metadata` to `GetBlobsAsync`; add `GetMetadataAsync`. Additive only — existing `ListAsync` callers unaffected. | dotnet, azure-storage, clean-architecture | §4 T9 |
| T10 | 3 | Application tests | Author the RED T11 coordinator tests plus the behavior-parity assertions for the upload path. | test-authoring, xunit, moq | authors §4 T11 |
| T11 | 3 | `SearchChunkIndexingCoordinator.cs` (new), `ISearchChunkIndexingCoordinator.cs` (Contracts), `ProcessorArtifactService.cs` | Extract the post-job-resolution indexing body into the shared coordinator (D12) and delegate from `ProcessorArtifactService`. Pure extraction; upload-path behavior unchanged. | dotnet, clean-architecture, refactoring | §4 T11 |
| T12 | 3 | Application tests | Author the RED T13 sweep tests (heal / increment / terminal-by-attempts / terminal-by-window / skip-terminal / ignore-non-orphan / continue-on-error / never-delete). | test-authoring, xunit, moq | authors §4 T13 |
| T13 | 3 | `OrphanedArtifactSweepService.cs` (new), `IOrphanedArtifactSweepService.cs` (Contracts), `OrphanedArtifactDto`/`OrphanSweepResultDto` (Contracts.Models), `IngestionOptions.cs` | Implement the sweep state machine per §3 steps 3-4 with the D9 bounds; add the three options with defaults. | dotnet, clean-architecture, azure-storage | §4 T13 |
| T14 | 3 | Application tests | Author the RED T15 hosted-service tests using the `GraphIngestionBackgroundServiceTests` reflection technique. | test-authoring, xunit, moq | authors §4 T15 |
| T15 | 3 | `OrphanedArtifactSweepBackgroundService.cs` (new), `DataPipelineConfiguration.cs` | Implement the hosted service on the `JobDeletionBackgroundService` shape (D11) and register it alongside the existing hosted services. | dotnet, aspnetcore, hosted-services | §4 T15 |
| T16 | 4 | API tests | Author the RED T17 endpoint tests (list, on-demand sweep, adopt), including the `mcr-api-admin` authorization and job-id validation assertions. | test-authoring, xunit, moq | authors §4 T17 |
| T17 | 4 | `IngestionJobsController.cs` | Add `GET artifacts/orphaned`, `POST artifacts/orphaned/sweep`, and `POST artifacts/orphaned/{uploadId}/adopt` (D15), thin over the sweep service. No discard/delete endpoint. | dotnet, aspnetcore, security | §4 T17 |
| T18 | 4 | `6-Docs/` | Plan closeout: verify acceptance criteria against implementation evidence, update the API, local-processing-service and ingestion canonical docs with the orphan lifecycle and admin surface, maintain the plan index. | documentation | §4 T18 — `no-test` |

## 6. Sequencing / dependency graph

Every implementation task is gated on its RED tests existing and failing first.

- **Phase 1 (independently shippable):** T1 → T2, T3, T4 (T3 before T4; T2 anytime after T1). T5 is independent of T2-T4 and may run in parallel (different language and test project).
- **Phase 2:** T6 → T7 (T7 requires T3's return-value change). T8 → T9; the T8/T9 pair has disjoint file scope from T6/T7 and may run in parallel with it.
- **Phase 3:** T10 → T11 (requires T3, T7). T12 → T13 (requires T9 for metadata reads and T11 for the coordinator). T14 → T15 (requires T13).
- **Phase 4:** T16 → T17 (requires T13 for listing/adopt and T15 for the on-demand trigger path). T18 last, after every other task is GREEN and reviewed.

Parallelizable test-authoring pairs with disjoint file scope: (T1, T5), (T6, T8), (T10, T12) once their prerequisites are GREEN.

## 7. Residual decisions / risks

- **R1 — Multi-instance metadata races.** `SetMetadataAsync` sets no `BlobRequestConditions`, so two API instances sweeping concurrently are last-write-wins on `orphanAttempts`. Degrades safely (terminal reached sooner; re-indexing idempotent). Resolved by adding ETag preconditions only if operational evidence shows premature terminal transitions. Owner: operations, on evidence.
- **R2 — Azure Monitor alert rule.** The `LogError` EventIds are the intended alert binding, but creating the rule is Pulumi/CI-CD infrastructure work agents may not perform. Owner: infrastructure change, tracked separately from this plan.
- **R3 — The sweep cannot heal a permanently jobless artifact (D8).** By design, resolution for that class is the D15 adopt action. If that class dominates in production, the correct follow-up is fixing the upstream job-creation race, not loosening D1. Owner: resolved by production evidence after Phase 3 ships.
- **R4 — Coordinator extraction touches the pinned happy path.** T11 changes `ProcessorArtifactService`'s constructor, so `ProcessorArtifactServiceTests` construction is affected. Mitigated by requiring every pre-existing upload-path assertion to stay green (T11 parity row).
- **R5 — Orphan blobs predating this change carry no marker** and are invisible to the sweep. A one-off backfill (stamp `state=Orphaned` on `search-chunks` blobs with no catalog row) is deliberately not in this plan. Owner: user, to confirm whether historical orphans are known to exist; if so it becomes its own dated plan.
- **R6 — Discard/delete action deferred (D15).** Operators can list and adopt but cannot remove a terminal orphan through the API. Owner: revisit once the orphan population is understood.
- **R7 — `uploadId` route constraint hardening.** `AdoptOrphanedArtifactAsync` in `IngestionJobsController.cs` has no `{uploadId:guid}` route constraint and does not use the existing `IngestionJobValidator.ValidateUploadId` pattern that all sibling endpoints in this controller use. Not exploitable today (no unsafe HTML consumers exist), but inconsistent with established validation conventions. Owner: follow-up hardening pass to add route constraint and validator reuse.

## 8. Out of scope

- Reversing or loosening fail-closed (D1) — settled; belongs nowhere.
- Auto-deleting terminal orphan blobs (D10) and the operator discard endpoint (R6) — data-loss risk; deferred to its own decision.
- Backfilling markers onto pre-existing orphan blobs (R5) — a one-off operational task, not product behavior.
- Creating the Azure Monitor alert rule (R2) — infrastructure delivered via Pulumi CI/CD.
- Replacing residual per-call `LogSanitizer.Sanitize` usage with the central `SanitizingLoggerProvider` (D13) — unrelated cleanup.
- Root-causing whatever upstream race leaves jobs missing at upload time — this plan makes it visible and recoverable; diagnosing it needs its own investigation.

## 9. Required skills

test-authoring, xunit, moq, dotnet, aspnetcore, clean-architecture, refactoring, hosted-services, azure-storage, logging, security, python, pytest, documentation.

## 10. Verification harness

The plan is done only when:

- **(a)** every §4 Test-contract test is GREEN, and the full `MotorcycleRAG.Application.Tests`, `MotorcycleRAG.API.Tests` and `MotorcycleRAG.Persistence.Tests` projects plus the Python suite pass with no regressions;
- **(b)** `code-reviewer` returned APPROVED for every implementation task, explicitly confirming that no code path passes `Guid.Empty` or `string.Empty` anchors into `IndexFromJsonlAsync` (D1) and that every `SetMetadataAsync` call writes the complete intended key set;
- **(c)** `security-review` passed for T17 (new admin endpoints: `mcr-api-admin` policy, rate limiting, adopt-endpoint job-id validation, no PII or blob SAS leakage in the orphan listing) and T5 (no secret or PII in the new client log line). Post-archival formal security-review skill run (2026-08-04) confirmed: no high-confidence vulnerabilities. Minor hardening note recorded in R7.
- **(d)** whole-solution Release build reports 0 errors / 0 warnings;
- **(e)** read-only `azure-reader` validation confirms, post-deploy, that a `search-chunks` blob stamped `state=Orphaned` is observable and that no terminal orphan was deleted. Agents may not deploy; this gate runs after CI/CD ships the change.

Refactors (notably T11) must keep all contract tests green.
