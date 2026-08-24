# Anchor-ID Feature Debt Cleanup

**Status:** Archived 2026-08-02
**Date:** 2026-08-02
**Goal:** Remove the leftover dead code and implementation debt left in the wake of the archived "Vector Store / SQL Graph Anchor-ID Metadata Contract" feature (archived plan `6-Docs/archive/plans/2026-08-01-vector-graph-anchor-id-contract.md`). Every task is behavior-preserving **except T9**, the one deliberately in-scope `SearchResult.Id`/`Content` deserialization fix (approved decision D5); every deletion is proven safe by evidence (file:line) against current source.

---

## 1. Problem / Motivation

The anchor-ID feature shipped GREEN, code-reviewed APPROVED, security-reviewed PASS, and was archived 2026-08-02. Its §11 Closeout carried 9 residual operator/CI follow-ups. This plan is the **engineering-cleanup** pass over those residuals. Investigation was performed with CodeGraph (`codegraph_explore`) as the primary discovery tool per root `AGENTS.md`, supplemented by direct `Read` where CodeGraph truncated or returned the wrong file. No grep/shell fallback was needed for code structure.

### Debt inventory (each verified against current source)

**D1 — `GraphEntityIngestionService` legacy 3-parameter constructor (the trigger).**
- `2-Application/MotorcycleRAG.Application/Services/Ingestion/GraphEntityIngestionService.cs:32-46` — the 4-param ctor DI resolves (XML doc 27-31: *"This is the constructor the DI container resolves at runtime"*).
- `…:48-65` — legacy 3-param ctor delegating `_manualDocumentRepository = null`; XML doc (48-52) is **stale** (*"Legacy constructor retained so existing callers and pre-T6 tests … continue to compile and run"* — T6 is long landed).
- `…:23-24` — stale field comment; `…:195-201` — stale XML doc on `StampSourceContentHashAsync`; `…:206-208` — `if (_manualDocumentRepository is null) { return; }` silent no-op reachable only via the legacy ctor.
- **Proof no production caller uses the 3-param ctor:** `1-Presentation/MotorcycleRAG.API/Configuration/Services/DataPipelineConfiguration.cs:57` registers `AddScoped<IGraphEntityIngestionService, …GraphEntityIngestionService>()` (interface-to-impl); `IManualDocumentRepository` is registered and resolved by siblings, so DI selects the 4-param ctor. CodeGraph's instantiation blast radius lists only test methods. Mirrors the `ChunkReprocessService` cleanup already done by T14 (`ChunkReprocessService.cs:39,48`).

**D2 — Duplicated `ResolveSourceContentHashAsync`.**
- `ProcessorArtifactService.cs:416-428` — `private` copy with **self-contradicting** XML doc (*"exists for shared consumption by T8 and T13"* — but it is `private`).
- `ChunkReprocessService.cs:209-229` — byte-identical `private` copy (*"Mirrors `ProcessorArtifactService.ResolveSourceContentHashAsync`"*). Bodies identical: null when no `ManualDocumentId`; `document?.SourceContentHash` otherwise; never `string.Empty`. This is the divergence that caused the §7 null-clobber finding.

**D3 — `SqlGraphRepository.GetNeighboursAsync` filtered/unfiltered literal duplication (deferred T18 fold).**
- `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/SqlGraphRepository.cs:386-402` — two near-identical SELECTs differing only by trailing `AND e.[RelationshipType] = @RelFilter;`. T18's MERGE consolidation (29-44) is the precedent; `SearchNodesAsync` (347-356) is the conditional-`AND` pattern to mirror. T18 reverted this fold because a test asserts on exact SQL text.

**D4 — The `Sql/Migrations/` folder executes nowhere (deployment-orphaned).** → Resolved: **retire** (D-decision below).

**D5 — `SearchResult.Id`/`Content` deserialize empty for camelCase index hits (pre-existing, surfaced by the feature).** → Resolved: **in scope** (D-decision below).
- `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/SearchResult.cs:12-72` — the three anchor props (41-43, 50-52, 59-61) carry `[JsonPropertyName]`+`[JsonIgnore(WhenWritingNull)]`; their XML doc (35-40) states the Azure.Search.Documents deserializer uses plain System.Text.Json with no camelCase policy. The pre-existing `Id` (13-14) and `Content` (16-17) have **no** `[JsonPropertyName]` → by the anchor props' own logic they don't map to the index's `id`/`content` fields.
- **Index schema confirmed camelCase** (`7-Deployment/infrastructure/Program.cs:1073,1075`: `["name"] = "id"` / `"content"`). `SearchResult` projects only `Id`/`Content` as index-backed fields (the rest — `RelevanceScore`, `Source`, `GeneratedAt`, `Highlights`, `Metadata` — are computed in code at `AzureSearchQueryService.ExecuteSearchAsync:177-203`, not index fields). **Fix scope is narrow: `[JsonPropertyName]` on `Id` and `Content` only.**
- **API wire-name risk eliminated:** the API applies a global camelCase policy (`1-Presentation/MotorcycleRAG.API/Configuration/JsonConfigurationExtensions.cs:21-57` ← `0-Base/MotorcycleRAG.Core/Utilities/JsonSerializationConfiguration.cs::DefaultOptions`, with passing test `DefaultOptions_Serialize_UsesCamelCasePropertyNamesAndOmitsNullValues`). So `[JsonPropertyName("id")]`/`["content"]` is a **no-op for outbound API JSON** (same wire name) — no API-client break. Residual risk = the **cache serializer** (query-cache services), which T9 must verify.

**D6 — Stale T7 RED scaffolding in `ChunkIndexingServiceTests`.**
- `5-Test/MotorcycleRAG.Persistence.Tests/Azure/Search/ChunkIndexingServiceTests.cs:764-780` — comment block headed *"T7 (RED)"* saying *"Once T7 is implemented, replace these with a direct call."* Helpers `FindAnchorParameterOverload` (782-786) / `HasAnchorParameterShape` (788-794) + reflection-invoke body (840-912) are the leftover scaffolding. The T14 contract test (800-838) is **intentional** reflection and stays.

**D7 — T8 null-hash assertion test gap.** `ProcessorArtifactServiceTests` has no dedicated assertion that a no-`ManualDocument` job passes `sourceContentHash: null` (not `string.Empty`).

**D8 — `graph_extractor.extract()` `str` overload + char-budget splitter production-dead; docstring stale.** *(Hunt finding.)* → Resolved: **remove** (D-decision below).
- `graph_extractor.py:364-481` — `extract()` accepts `list[tuple[str,str]] | str`; the `str` branch (390-422) splits via `_split_into_batches` (73-83). Docstring (381-386) claims *"pdf_processor.py still calls this way as of this change — migrating it is a separate, downstream task."*
- **Contradiction:** `pdf_processor.py:848-851` calls `extract([(records[i]["id"], chunk.text) for …], …)` — the chunk-list contract (T2 DONE). The `str` branch + `_split_into_batches` have no production caller.

### Cleared during investigation (no task)

- **`sourceChunkIds` not parsed on the .NET side is correct-by-design.** `_merge_results` (`graph_extractor.py:171-173`) emits it; the Python layer consumes it to emit `SOURCED_FROM` edges (`pdf_processor.py:867-908`). The .NET `GraphNodeJson` deliberately omits it (`GraphEntityIngestionService.cs:182-184,343-354`).
- **`[JsonIgnore]`/`[JsonPropertyName]` consistency across the three anchor-bearing types is CLEAN.** `ChunkIndexRecord` (`ChunkIndexingService.cs:477-564`), `ChunkAnchorUpdateDocument` (`ChunkAnchorBackfillService.cs:30-37`), `SearchResult` anchors (`SearchResult.cs:41-61`) all apply both attributes identically.

## 2. Approved decisions

- **D-d1 (was §2a Q1).** **Retire the `Sql/Migrations/` folder.** Delete the 7 standalone `*.sql` files; `schema.sql` becomes the single deployed source; update `database-schema.md`. Eliminates the latent `OBJECT_ID` ordering hazard in `GraphNodeAnchorColumnsMigration.sql` and the trap for the next author.
- **D-d2 (was §2a Q2).** **Fix the `SearchResult.Id`/`Content` camelCase deserialization gap IN SCOPE** (T9). Scope is narrow: add `[JsonPropertyName("id")]` and `[JsonPropertyName("content")]` to those two properties only (the sole index-backed `SearchResult` fields). API-client wire names are unchanged (global camelCase policy already in force). The cache serializer must be verified via a round-trip test (T9 acceptance). This is the plan's **only behavior-changing task** — pre-existing empty `Id`/`Content` will populate.
- **D-d3 (was §2a Q3).** **Remove the production-dead `str` overload + `_split_into_batches`** from `graph_extractor.extract()`; narrow the signature to `list[tuple[str,str]]`; rewrite the stale docstring (T6). Parallel to D1 / the T14 overload-removal pattern.
- **D-d4 (was §2a Q4).** **Execute the deferred T18 `GetNeighboursAsync` literal fold** test-first (T4): rewrite the text-coupled test to assert SQL semantics, then fold the two literals into one conditional-`AND` mirroring `SearchNodesAsync`.

## 2a. Open questions (decision ledger)

All four resolved → §2. No OPEN rows remain.

## 3. Investigation findings

(Summarized in §1 evidence; key cross-cutting points:)
- DI registration is interface-to-impl (`DataPipelineConfiguration.cs:57`) → 4-param ctor is the sole runtime path; the 3-param ctor has only test callers (D1).
- `ChunkReprocessService` already requires `IManualDocumentRepository` (`ChunkReprocessService.cs:39,48`) — the exact shape D1's cleanup produces (D1).
- A shared hash-resolution helper belongs in the **Application** layer (consumes `IManualDocumentRepository` [Contracts] + `IngestionJob`/`ManualDocument` [Domain]); both consumers already inject the repository, so an `internal static` helper is the lightest home (D2).
- T18's `UpsertNodeMergeSql` consolidation is the precedent for D3; `SearchNodesAsync` ternary is the conditional-`AND` pattern.
- Index fields are camelCase (`Program.cs:1073-1097`); `SearchResult` projects only `Id`/`Content` as index-backed; API uses global camelCase policy (`JsonConfigurationExtensions.cs:21-57`) → `[JsonPropertyName("id")]`/`["content"]` is serialization-neutral for the API path (D5).
- `pdf_processor.py:848-851` proves the chunk-list migration is done → `extract()` `str` branch is production-dead (D8).

## 4. Test contract

| Task # | Test project / file | Runner command | Behavior asserted (RED → GREEN) |
|--------|---------------------|----------------|---------------------------------|
| T1 | `5-Test/MotorcycleRAG.Application.Tests/Services/Ingestion/GraphEntityIngestionServiceTests.cs` + new reflection assertion | `dotnet test 5-Test/MotorcycleRAG.Application.Tests/MotorcycleRAG.Application.Tests.csproj --filter FullyQualifiedName~GraphEntityIngestionServiceTests` | (a) New reflection assertion: `GraphEntityIngestionService` declares **exactly one** public ctor of 4 params (`IBlobStorageService`, `IGraphRepository`, `IManualDocumentRepository`, `ILogger<>`). RED while the 3-param ctor exists; GREEN once deleted. (b) `Constructor_WithNullDependencies_ThrowsArgumentNullException` + `CreateSut`/`CreateSutWithAllDependencies` migrated to the 4-param ctor and pass. (c) Existing T6 behavior tests (Chunk-quarantine, `SourceContentHash` stamping, edge endpoint rules) remain GREEN unchanged. |
| T2 | new `5-Test/MotorcycleRAG.Application.Tests/Services/Ingestion/SourceContentHashResolverTests.cs` + existing `ProcessorArtifactServiceTests`/`ChunkReprocessServiceTests` as regression net | `dotnet test 5-Test/MotorcycleRAG.Application.Tests/MotorcycleRAG.Application.Tests.csproj --filter "FullyQualifiedName~SourceContentHashResolver|FullyQualifiedName~ProcessorArtifactService|FullyQualifiedName~ChunkReprocessService"` | Shared-helper unit tests: returns `null` when `job.ManualDocumentId` is null; `null` when `GetDocumentByIdAsync` returns null; the document's `SourceContentHash` when present; **never** `string.Empty`. Existing `ProcessorArtifactServiceTests`/`ChunkReprocessServiceTests` hash-resolution tests remain GREEN with zero assertion changes (regression net). |
| T3 | `5-Test/MotorcycleRAG.Persistence.Tests/Azure/Search/ChunkIndexingServiceTests.cs` | `dotnet test 5-Test/MotorcycleRAG.Persistence.Tests/MotorcycleRAG.Persistence.Tests.csproj --filter FullyQualifiedName~ChunkIndexingServiceTests` | **Refactor (no new behavior).** `IndexFromJsonlAsync_WhenCalledWithAnchorParameters_ShouldStampEveryDocument…` rewritten to invoke `IndexFromJsonlAsync` via **direct call** (no reflection), asserting the SAME captured-batch anchor values; stale `T7 (RED)` block (764-780) + reflection-invoke scaffolding (`FindAnchorParameterOverload`/`HasAnchorParameterShape` + args-invoke 894-912) removed. T14 contract test (800-838) untouched, stays GREEN. Validation gate: full `ChunkIndexingServiceTests` suite GREEN, only edits = direct-call conversion + comment deletion. |
| T4 | `5-Test/MotorcycleRAG.Persistence.Tests/Persistence/Sql/Repositories/SqlGraphRepositoryTests.cs` | `dotnet test 5-Test/MotorcycleRAG.Persistence.Tests/MotorcycleRAG.Persistence.Tests.csproj --filter FullyQualifiedName~SqlGraphRepositoryTests` | Rewrite the `GetNeighboursAsync` test(s) that assert exact SQL text (the `.Contain("AND n1.[Id] = @NodeId;")`-style assertion) to assert **semantics**: (1) filtered call binds `@NodeId` + `@RelFilter`, unfiltered binds only `@NodeId`; (2) filtered `CommandText` contains the relationship predicate, unfiltered does not; (3) fake-reader mapping round-trips `FromChunkId`/`ToChunkId`/`FromSourceContentHash`/`ToSourceContentHash` for both paths. RED: rewritten test authored **before** the fold, passes against current two-literal code; GREEN: after folding to one conditional-`AND` (mirror `SearchNodesAsync`), same test passes with **zero** post-fold edits. |
| T5 | `5-Test/MotorcycleRAG.Application.Tests/Services/Ingestion/ProcessorArtifactServiceTests.cs` (new method) | `dotnet test 5-Test/MotorcycleRAG.Application.Tests/MotorcycleRAG.Application.Tests.csproj --filter FullyQualifiedName~ProcessorArtifactServiceTests` | **Regression guard (asserts existing correct behavior).** Given a job with `ManualDocumentId == null`, captured `IChunkIndexingService.IndexFromJsonlAsync` received `sourceContentHash: null` — explicitly null, **not** `string.Empty`. |
| T6 | `5-Test/local-processing-service.Tests/test_graph_extractor.py` | `cd 2-Application/local-processing-service && .venv/bin/python -m pytest -c pyproject.toml --rootdir=. ../../5-Test/local-processing-service.Tests/test_graph_extractor.py -v` | (a) str-input test cases removed/migrated to `list[tuple[str,str]]`. (b) New test: `extract("some text")` raises `TypeError` (signature narrowed). (c) Existing chunk-batching / `sourceChunkIds`-union / no-partial-chunk tests remain GREEN. `_split_into_batches` has no remaining reference. |
| T7 | no automated test (pure deletion + doc edit) | **Validation replacement:** `dotnet build MotorcycleRAG.sln -c Release` (0 errors/0 warnings) confirms deleting `…/Sql/Migrations/*.sql` breaks no compile reference (not embedded resources); `docs-dev` confirms `database-schema.md` no longer enumerates a migrations folder and states `schema.sql` is the single deployed source; read-only scan confirms no `sqlcmd`/DbSetup path references the deleted files. | Standalone migration files are historical-only and execute nowhere; deleting removes the documented trap + the `OBJECT_ID` hazard. |
| T9 | `5-Test/MotorcycleRAG.Application.Tests/Services/AzureSearchQueryServiceTests.cs` (existing real-deserialization file) + new serialization round-trip test | `dotnet test 5-Test/MotorcycleRAG.Application.Tests/MotorcycleRAG.Application.Tests.csproj --filter "FullyQualifiedName~AzureSearchQueryService"` | **(1) RED (deserialization repro):** extend the existing real-deserialization harness with a test feeding a fake Search response whose document JSON uses camelCase keys `{"id":"abc","content":"body"}` and assert the returned `SearchResult.Id == "abc"` and `Content == "body"` (not empty). RED today (case-sensitive SDK deserializer); GREEN after `[JsonPropertyName("id")]`/`["content"]` added to `SearchResult.cs:13-17`. **(2) Serialization non-regression (cache gate):** serialize a populated `SearchResult` under `JsonSerializationConfiguration.DefaultOptions` (API policy) AND under whichever `JsonSerializerOptions` the query-cache services (`DistributedQueryCacheService`/`MemoryQueryCacheService`) use — then deserialize and assert `Id`/`Content` round-trip. If the cache uses default (PascalCase) options, the implementer MUST confirm the round-trip still preserves `Id`/`Content` (the explicit attribute makes the key stable across both policies); if it does not, surface as a §7 residual before merging. No `SearchResult` property other than `Id`/`Content` receives a new attribute (other fields are code-computed, not index-backed). |
| T8 | No automated test (documentation closeout). | **Manual validation:** `docs-dev` diffs updated canonical docs against the as-built code and maintains the plan index per root `AGENTS.md` before archiving. | Standard `docs-dev` plan-closeout gate. |

## 5. Task list

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| T1 | Cleanup | `GraphEntityIngestionService` (.NET Application) | Delete 3-param ctor (48-65); make `_manualDocumentRepository` non-nullable (field 24); remove dead null-guard (206-208); fix stale field comment (23) + stale XML docs (ctor 48-52, `StampSourceContentHashAsync` 197-198). Migrate the one test helper using the 3-param ctor. File scope: `2-Application/MotorcycleRAG.Application/Services/Ingestion/GraphEntityIngestionService.cs` + affected test. Refs Test-contract T1. | .NET Application (C#, xUnit) |
| T2 | Cleanup | `ProcessorArtifactService` + `ChunkReprocessService` (.NET Application) | Extract shared hash helper (`internal static`, Application layer); delete both `private` copies (`ProcessorArtifactService.cs:416-428`, `ChunkReprocessService.cs:209-229`); fix the self-contradicting XML doc; both consumers call the helper. Refs Test-contract T2. *(Implementer also verifies/tidies Moq `It.IsAny<string>()` vs `It.IsAny<string?>()` near `sourceContentHash`/`manualDocument` setups while touching these test files — T14 reviewer note.)* | .NET Application (C#, xUnit) |
| T3 | Refactor | `ChunkIndexingServiceTests` (.NET Persistence tests) | Delete stale `T7 (RED)` block (764-780); convert reflection-invoke behavior test (840-912) to a direct call; delete `FindAnchorParameterOverload`/`HasAnchorParameterShape` (782-794) if unused post-conversion (T14 contract test 800-838 has its own inline reflection, stays). Refs Test-contract T3. | .NET test (xUnit, reflection) |
| T4 | Refactor | `SqlGraphRepository.GetNeighboursAsync` (.NET Persistence) | Fold the two duplicated SELECT literals (386-402) into one conditional-`AND` mirroring `SearchNodesAsync` (347-356). Test-first per Test-contract T4. | .NET Persistence (Dapper, T-SQL, xUnit) |
| T5 | Test backfill | `ProcessorArtifactServiceTests` (.NET Application tests) | Add dedicated `sourceContentHash == null` (not empty) regression guard for the no-`ManualDocument` job. Refs Test-contract T5. | .NET test (xUnit, Moq) |
| T6 | Cleanup | `graph_extractor.py` (Python) | Narrow `extract()` to `chunks: list[tuple[str, str]]`; delete `str` branch (390-422) + `_split_into_batches` (73-83); rewrite stale docstring (381-386); migrate str-input tests. Refs Test-contract T6. | Python backend (pytest) |
| T7 | Cleanup / docs | `Sql/Migrations/` + `database-schema.md` | Delete the 7 standalone `…/Sql/Migrations/*.sql`; update `6-Docs/DevOps/database-schema.md` to state `schema.sql` is the single deployed source (remove the "seven idempotent migration files" enumeration). Refs Test-contract T7. | SQL/T-SQL + Technical documentation |
| T9 | Bug fix (in scope) | `SearchResult` (.NET Contracts.Models) | Add `[JsonPropertyName("id")]` to `SearchResult.Id` and `[JsonPropertyName("content")]` to `SearchResult.Content` (`SearchResult.cs:13-17`). No other property changes. Verify cache round-trip per Test-contract T9. **Only behavior-changing task** (D-d2). | .NET Contracts + .NET test (xUnit, Azure SDK fake harness) |
| T8 | Docs | Plan closeout | `docs-dev` verifies acceptance against as-built code, updates canonical docs, maintains plan index before archive. | Technical documentation |

## 6. Sequencing / dependency graph

```
T1 (GraphEntityIngestionService ctor)   ─┐
T2 (shared hash helper)                  ─┤
T3 (ChunkIndexingServiceTests refactor)  ─┤  file-scope-disjoint → parallel
T5 (T8 null-hash regression guard)       ─┤
T4 (GetNeighbours fold)                  ─┤  (own RED test first)
T6 (graph_extractor str-removal)         ─┤  (own RED test first)
T9 (SearchResult Id/Content fix)         ─┘  (own RED repro + cache round-trip first)
T7 (migrations folder retire + doc)      ─── independent (deletion + doc)
T8 (docs closeout)                       ─── last; depends on T1–T7, T9 landed
```

- T1, T2, T3, T5 are file-scope-disjoint → parallel from the start.
- T4, T6, T9 each carry their own RED test authored before implementation.
- T7 (deletion + doc) is independent and may run any time.
- Every implementation task references its Test-contract row; no implementation begins before its RED test exists.

## 7. Residual decisions / risks

- **T9 cache-serializer verification (must-close-before-merge).** T9's acceptance includes a serialization round-trip under the query-cache services' `JsonSerializerOptions`. The API path is serialization-neutral (global camelCase policy). If the cache serializer round-trip fails after adding the attributes, the implementer surfaces it as a residual rather than merging a regression. (Root smell: `SearchResult` is both SDK deserialize target and API/cache DTO — the minimal `[JsonPropertyName]` fix is per D-d2; a read-model/DTO separation is the larger refactor and is explicitly out of scope.)
- **T9 is behavior-changing.** Pre-existing search results that returned empty `Id`/`Content` will populate. This is the intended fix (D-d2), not a regression — but downstream consumers that quietly tolerated empty `Id`/`Content` should be re-checked by `code-reviewer`.
- **Carried from the archived plan (still open, not actioned here):** §10(d) post-deploy `azure-reader` Get-Index check (agents cannot deploy per D6); T12 backfill execution (operator/CI action); artifact-GUID churn on re-ingestion; no CI-reachable SQL Server (static-text/fake-harness tests only).

## 8. Out of scope

- **Full read-model/DTO separation for `SearchResult`** (the architecturally cleaner alternative to T9's minimal `[JsonPropertyName]` fix) — bigger refactor; D-d2 chose the minimal fix.
- **Full closed-vocabulary ontology enforcement** — separately tracked in `6-Docs/reference/knowledge-graph-ontology.md`.
- **CI-reachable SQL Server provisioning** (Testcontainers / `services:` container) — infrastructure change requiring explicit user approval; carried gap.
- **T12 backfill execution against live Azure AI Search** — operator/CI action; agents cannot deploy.
- **`bike_graph_processor.py`** — CSV-driven, no chunks/LLM/vectors.
- **Legacy full-document indexing path** (`MotorcycleDocumentDto`, `AzureSearchDocumentService`) — marked legacy.

## 9. Required skills

- .NET Application-layer engineering (C#, ASP.NET Core DI, xUnit/Moq) — T1, T2, T5
- .NET Persistence-layer engineering (Dapper, SQL Server Graph, xUnit fake-harness) — T4
- .NET test engineering (xUnit, reflection, Moq argument capture) — T3, T5
- .NET Contracts + Azure SDK deserialization (System.Text.Json, `[JsonPropertyName]`) — T9
- Python backend engineering (pytest, typing) — T6
- SQL Server T-SQL + schema/deployment ownership — T7
- Technical documentation (`docs-dev`) — T7 (doc edit), T8 (closeout)

## 10. Verification harness

The plan is done only when: (a) every Test-contract row is GREEN (T1–T6, T9); (b) `code-reviewer` returns APPROVED for every task, with particular attention to T9's downstream-consumer re-check and T1/T6 proving no production caller used the removed overloads; (c) `security-review` passes where applicable (T9 deserialization change; T7 schema-source change); (d) T7's build + doc-validation gate and T9's cache round-trip gate pass; (e) the `docs-dev` closeout (T8) verifies acceptance against the as-built code and updates canonical documentation before this plan is archived. Refactors (T3, T4) must keep their contract tests green with zero post-change test edits.

---

## 11. Closeout (docs-dev, 2026-08-02)

**Status:** Archived 2026-08-02 — every cleanup task is GREEN and code-reviewed APPROVED; canonical documentation updated by the `docs-dev` plan-closeout gate per root `AGENTS.md`. This plan is historical; the implementation is the truth.

### Green evidence (run 2026-08-02)

All cleanup tasks (T1, T2+T5, T3, T4, T6, T7, T9) are GREEN and code-reviewed APPROVED (T7's sole CHANGES REQUESTED was the `database-schema.md` doc update, delivered by this closeout):

- Whole-solution `dotnet build MotorcycleRAG.sln -c Release`: **0 errors / 0 warnings** (23 projects).
- Full projects: Contracts.Tests 43 + Persistence.Tests 1651 + Application.Tests 978 = **2671 tests, 0 failures, 0 warnings**.
- §4 Test-contract filters: T1 GraphEntityIngestionServiceTests = 15; T2+T5 SourceContentHashResolver + ProcessorArtifactService + ChunkReprocessService = 48; T3+T7 ChunkIndexingServiceTests + SqlGraphRepositoryTests + SchemaSqlAnchorColumnsTests = 102; T9 AzureSearchQueryService = 48.
- Python T6: `test_graph_extractor` = 48, `test_pdf_processor` (-k graph) = 3.

### Verification gates

| Gate | Verdict |
| --- | --- |
| (a) every §4 Test-contract row GREEN (T1–T6, T9) | ✅ GREEN |
| (b) `code-reviewer` APPROVED for every task (T9 downstream-consumer re-check; T1/T6 prove no production caller used the removed overloads) | ✅ APPROVED |
| (c) `security-review` passes where applicable (T9 deserialization change; T7 schema-source change) | ✅ PASS |
| (d) T7 deletion verified safe on all gates — both deploy paths (CI `sqlcmd` and DbSetup CLI) run only `schema.sql`; no embedded references to the deleted files; `schema.sql` complete | ✅ VERIFIED |
| (e) T9 cache round-trip + downstream-consumer gates | ✅ PASS |
| (f) `docs-dev` closeout (T8) verifies acceptance against the as-built code and updates canonical documentation | ✅ DONE (this closeout) |

### Behavior-change note (T9)

T9 fixed the pre-existing `SearchResult.Id`/`Content` deserialization bug by adding `[JsonPropertyName("id")]` / `[JsonPropertyName("content")]` to those two properties only. **Production Azure AI Search results now carry real `Id`/`Content`** — previously they deserialized silently empty. Cache round-trip verified under both the query-cache and API serializer policies; no downstream consumer depended on emptiness.

### Plan-table label reconciliation (T9)

The §4 T9 row lists the test runner as `MotorcycleRAG.Application.Tests`, but the test actually lives in `5-Test/MotorcycleRAG.Persistence.Tests/Azure/Search/AzureSearchQueryServiceTests.cs` (the service lives in Persistence). The `FullyQualifiedName~AzureSearchQueryService` filter resolves to that file; the Application.Tests label in the §4 table is a runner-label error, corrected here.

### Residual operator/CI follow-ups (carried forward from the archived feature plan, still open)

The cleanup resolved five of the feature plan's nine §11 residuals (feature-plan #3 → T1; #4 → T9; #5 → T4; #6 → T5; #8 → T7). The remaining four are carried forward unchanged:

1. **§10(d)** — `azure-reader` read-only Get-Index verification of the T9 anchor fields (`indexedArtifactId`, `ingestionJobId`, `sourceContentHash`, each `Edm.String`/`filterable`) on every category-partitioned index, to run AFTER CI/CD deploys (D6: agents do not deploy).
2. **T12 backfill execution** against live Azure AI Search — operator/CI action only after T9 deploys; `ChunkAnchorBackfillService` is author+test-only by design, no auto-trigger entry point exists.
3. **§7 carried gap — no CI-reachable SQL Server** — static-text/fake-harness tests are not live-SQL-Server verification; provisioning Testcontainers/`services:` SQL is a separate infra plan requiring explicit approval.
4. **Artifact-GUID churn on re-ingestion** (§7) — candidate follow-up to have `ProcessorArtifactService` reuse the existing artifact row's ID when `(UploadId, ArtifactType)` exists (mirroring `ChunkReprocessService`).
