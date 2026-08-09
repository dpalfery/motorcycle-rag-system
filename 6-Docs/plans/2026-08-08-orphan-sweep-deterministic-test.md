---
id: plans/2026-08-08-orphan-sweep-deterministic-test
title: 2026-08-08 Orphan-Sweep deterministic test via TimeProvider injection
doc-type: plan
status: current
component: MotorcycleRAG.Application
owner: Maintainers
last-reviewed: 2026-08-08
code-refs:
  - OrphanedArtifactSweepBackgroundService
  - OrphanedArtifactSweepBackgroundServiceTests
  - DataPipelineConfiguration
api-endpoints: []
decided-by: []
supersedes: []
---
# 2026-08-08 Orphan-Sweep deterministic test via TimeProvider injection

**Status:** Draft
**Date:** 2026-08-08
**Goal:** Make the flaky `OrphanedArtifactSweepBackgroundService` interval test fully clock-free and deterministic by injecting `TimeProvider` into the production sweep loop, matching the repo's established `SigningKeyCache` pattern.

---

## 1. Problem / Motivation

The production background-sweep loop is correct. The defect is a **flaky test**:

`ExecuteAsync_UsesConfiguredOrphanSweepIntervalBetweenCycles`
(`5-Test/MotorcycleRAG.Application.Tests/Services/Ingestion/OrphanedArtifactSweepBackgroundServiceTests.cs:271-294`).

It races a **200 ms real-time cancellation window** against `Times.AtLeast(3)` sweeps with a 30 ms interval. Under CI load only ~2 cycles complete before the token fires, so the `AtLeast(3)` assertion fails intermittently. Root cause: the test relies on wall-clock `Task.Delay`; nothing is wrong with the loop.

- **Production loop:** `2-Application/MotorcycleRAG.Application/Services/Ingestion/OrphanedArtifactSweepBackgroundService.cs:68-123`. `ExecuteAsync` reads `OrphanSweepInterval` from `IOptions<IngestionOptions>` (line 70), runs `RunSweepAsync`, then `await Task.Delay(sweepInterval, stoppingToken)` (line 113), then loops.
- The established repo fix pattern is `TimeProvider` injection: `SigningKeyCache` (`1-Presentation/MotorcycleRAG.API/Extensions/AuthenticationServiceExtensions.cs:27-38`) keeps a convenience overload that delegates `: this(..., TimeProvider.System)` and a real constructor taking `TimeProvider`, consumed in tests by `FakeTimeProvider` (`SigningKeyCacheTests.cs:4,188`).

## 2. Approved decisions

- **D1 (RECOMMENDED — awaiting confirmation): Constructor shape = delegating overload (Option A).** Add a 4-parameter constructor taking `TimeProvider` (null-guarded) and **retain the existing 3-parameter constructor**, delegating with `: this(..., TimeProvider.System)`. Replace the single `Task.Delay(sweepInterval, stoppingToken)` with `Task.Delay(sweepInterval, _timeProvider, stoppingToken)`. **Zero DI change** — `AddHostedService<>()` activates via the 3-parameter overload (the container cannot resolve an unregistered `TimeProvider`, so it picks the delegating overload), giving byte-identical production behavior. This matches the only in-repo precedent (`SigningKeyCache`) and introduces no new cross-cutting DI concern.
  - **Rejected alternative (Option B):** register `services.AddSingleton(TimeProvider.System)` globally and use a single required `TimeProvider` parameter. No repo precedent for a global `TimeProvider` registration; every existing usage passes `TimeProvider.System` explicitly. This would be a new cross-cutting DI concern requiring approval under AGENTS.md, with larger blast radius (every direct constructor site must change) for no behavioral benefit. Not recommended.
- **D2: Package = `Microsoft.Extensions.TimeProvider.Testing` 9.9.0** — reuse the exact version already referenced in `5-Test/MotorcycleRAG.API.Tests/MotorcycleRAG.API.Tests.csproj:39`. (Note: the prior investigation's package id `Microsoft.Extensions.Time.Testing` was imprecise; the actual id is `Microsoft.Extensions.TimeProvider.Testing`, namespace `Microsoft.Extensions.Time.Testing`.)
- **D3: Scope = fix only the one flaky test.** Convert only `ExecuteAsync_UsesConfiguredOrphanSweepIntervalBetweenCycles` to `FakeTimeProvider`. The other 6 behavioral tests stay on the 3-parameter overload (real time) — they are not count-sensitive and were not reported flaky.

## 3. Investigation findings

- **Production constructor (current), lines 53-65:**
  `public OrphanedArtifactSweepBackgroundService(IServiceScopeFactory scopeFactory, IOptions<IngestionOptions> ingestionOptions, ILogger<OrphanedArtifactSweepBackgroundService> logger)` with three `ArgumentNullException.ThrowIfNull` guards.
- **Production delay site:** line 113 `await Task.Delay(sweepInterval, stoppingToken).ConfigureAwait(false);`.
- **DI registration (sole site):** `1-Presentation/MotorcycleRAG.API/Configuration/Services/DataPipelineConfiguration.cs:60` `services.AddHostedService<OrphanedArtifactSweepBackgroundService>();`. Container-resolved; no direct production `new`.
- **No global `TimeProvider` DI registration exists.** All existing usages pass `TimeProvider.System` explicitly: `AuthenticationServiceExtensions.cs:28` (delegating overload), `AppConfigurationExtensions.cs:85,97` (API), `AppConfigurationExtensions.cs:117` (BFF). Native `Task.Delay(TimeSpan, TimeProvider)` already used by the BFF (`AppConfigurationExtensions.cs:141,145`).
- **TFM = `net10.0`** → native `Task.Delay(TimeSpan, TimeProvider, CancellationToken)` overload is available (added .NET 9). No polyfill needed.
- **No central package management.** There is no `Directory.Packages.props` / `Packages.props`; `ManagePackageVersionsCentrally` is unset. Package versions are per-`PackageReference`. Add the new dep as a normal `<PackageReference>` in the test csproj.
- **`FakeTimeProvider` already in repo:** `Microsoft.Extensions.TimeProvider.Testing` 9.9.0 in API.Tests; consumed in `SigningKeyCacheTests.cs`.
- **Full list of direct constructors of `OrphanedArtifactSweepBackgroundService`** (all 3-parameter today), in `OrphanedArtifactSweepBackgroundServiceTests.cs`:
  | # | Line | Test method | Update under Option A |
  |---|------|-------------|-----------------------|
  | 1 | 115 | `ExecuteAsync_WhenCycleRuns_ResolvesSweepServiceFromScopeAndCallsRunSweepAsync` | Unchanged (3-param overload) |
  | 2 | 138 | `ExecuteAsync_WhenMultipleCyclesRun_ResolvesFreshScopeEachCycleAndDisposesIt` | Unchanged |
  | 3 | 173 | `ExecuteAsync_WhenRunSweepAsyncThrows_LogsErrorAndContinuesToNextCycle` | Unchanged |
  | 4 | 204 | `ExecuteAsync_WhenStoppingTokenAlreadyCancelled_ExitsCleanlyWithoutRunningCycle` | Unchanged |
  | 5 | 227 | `ExecuteAsync_WhenCancelledDuringInterCycleDelay_ExitsGracefullyWithoutThrowing` | Unchanged |
  | 6 | 257 | `ExecuteAsync_WhenCancelledBetweenCycles_StopsBeforeStartingNextCycle` | Unchanged |
  | 7 | **281** | **`ExecuteAsync_UsesConfiguredOrphanSweepIntervalBetweenCycles` (FLAKY)** | **Convert to 4-param ctor + `FakeTimeProvider`; rewrite body deterministic** |
  | 8 | 303 | `Constructor_WithNullArguments_ThrowsArgumentNullException` | Unchanged 3 cases; **add 4th case** `new ...(scopeFactory.Object, options, logger.Object, null!)` → throws `timeProvider` |
  | 9 | 306 | (same test, 2nd null case) | Unchanged |
  | 10 | 309 | (same test, 3rd null case) | Unchanged |
  Plus the DI activation at `DataPipelineConfiguration.cs:60` (no `new`). **No other constructors exist in production or any host.**

## 4. Task list

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| 1 | Production | `OrphanedArtifactSweepBackgroundService.cs` | Add `private readonly TimeProvider _timeProvider;`. Add 4-param constructor `(IServiceScopeFactory, IOptions<IngestionOptions>, ILogger<>, TimeProvider)` with `ArgumentNullException.ThrowIfNull(timeProvider)` and field assignment. Change existing 3-param ctor to delegate `: this(scopeFactory, ingestionOptions, logger, TimeProvider.System)`. Replace line 113 with `await Task.Delay(sweepInterval, _timeProvider, stoppingToken).ConfigureAwait(false);`. No other behavior change. | dotnet-dev |
| 2 | Test infra | `MotorcycleRAG.Application.Tests.csproj` | Add `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="9.9.0" />` (reuse API.Tests version). | test-dev |
| 3 | Test | `OrphanedArtifactSweepBackgroundServiceTests.cs:271-294` (flaky method) | Rewrite `ExecuteAsync_UsesConfiguredOrphanSweepIntervalBetweenCycles` to be clock-free using `FakeTimeProvider` (see Test contract). Pass `FakeTimeProvider` via the 4-param ctor (line 281). Assert exact deterministic count. | test-dev |
| 4 | Test | `OrphanedArtifactSweepBackgroundServiceTests.cs:297-310` (`Constructor_WithNullArguments_ThrowsArgumentNullException`) | Add a 4th assertion: `new ...(scopeFactory.Object, options, logger.Object, null!)` throws `ArgumentNullException` with parameter name `timeProvider`. Leave the existing 3 cases (they exercise the 3-param delegating overload). | test-dev |
| 5 | Verify | `5-Test/MotorcycleRAG.Application.Tests` | Build whole solution Release (0 errors/0 warnings); run the Application.Tests suite and confirm 0 failures. Re-run the converted test in a loop (e.g. `-r 50`) to confirm non-flaky. | test-dev |
| 6 | Review | branch diff | `code-reviewer` review; `security-review` (none expected — no secret/I-O surface, time-abstraction only). | code-review, security-review |
| 7 | Docs closeout | `6-Docs/plans/README.md` + canonical docs | `docs-dev` verifies acceptance criteria against implementation, updates component docs if the public constructor surface is documented, archives plan. | docs-dev |

### Test contract (defines done-ness)

`ExecuteAsync_UsesConfiguredOrphanSweepIntervalBetweenCycles` becomes fully deterministic:

- Construct the service via the **4-param ctor** with a `FakeTimeProvider`.
- Choose a **distinctive `configuredInterval`** (e.g. `TimeSpan.FromMilliseconds(42)`) — distinct from the 5-minute default and from any plausible hardcoded value, via `CreateOptions(configuredInterval)`.
- Run `ExecuteAsync` (via the existing reflection helper `InvokeExecuteAsync`) on a **background `Task.Run`** with a `CancellationToken` the test controls (no wall-clock timeout).
- For each of `N` additional cycles (e.g. `N = 3`): await a synchronization signal that the sweep mock was entered, then `fakeTime.Advance(configuredInterval)`. `FakeTimeProvider` completes the pending `Task.Delay(interval, timeProvider, ct)` only when the clock reaches its due time.
- Cancel the token, await the loop to settle (swallow the expected `OperationCanceledException`).
- **Assert:** `sweepServiceMock.Verify(s => s.RunSweepAsync(It.IsAny<CancellationToken>()), Times.Exactly(1 + N))`.

**Why this also proves the interval value:** after advancing exactly `1 + N − 1 = N` times by exactly `configuredInterval`, an exact count of `1 + N` sweeps holds **only if** the loop's delay equals `configuredInterval`. A longer interval (e.g. the 5-minute default) would not release the delay → fewer sweeps → fail. A shorter hardcoded interval would release multiple delays per advance → more sweeps → fail. Thus `Times.Exactly(1 + N)` pins both count and interval in one assertion. The wall-clock `200 ms` window is gone.

**Failure-mode gate:** on the **unmodified** production code (real `Task.Delay`, no `TimeProvider`), this test cannot be written as specified and the existing flaky assertion remains — i.e. the contract fails RED without task 1 and passes GREEN with it.

## 5. Sequencing / dependency graph

```
T1 (production ctor + delay) ─┬─► T3 (rewrite flaky test) ─┐
                              └─► T4 (ctor-null 4th case)  ─┤
T2 (test package ref) ───────────────────────────────────► T3 (FakeTimeProvider needs the package)
T3, T4 ─► T5 (build + run + repeat-run) ─► T6 (reviews) ─► T7 (docs closeout)
```

- **T1 must land with (or before) T3 and T4** — the 4-param ctor is the contract both rely on. (T3/T4 RED scaffolding depends on T1.)
- **T2 must land with (or before) T3** — `FakeTimeProvider` requires the package reference.
- T1, T2 are independent of each other and can run in parallel.
- Recommended single-PR delivery: T1+T2+T3+T4 together, then T5, then T6.

## 6. Residual decisions / risks

- **D1 (constructor shape) is the only open decision** — see §2. Recommended Option A. Owner: human confirmation.
- **Risk — DI constructor selection ambiguity:** the default `Microsoft.Extensions.DependencyInjection` container selects the constructor with the most parameters it can fully resolve. With Option A and no `TimeProvider` registration, only the 3-param overload is resolvable → deterministic selection, identical to today. If a future change registers `TimeProvider` globally, the container would then prefer the 4-param overload (still resolving to the same `TimeProvider.System`) — no breakage. (T5 build confirms no DI activation error at startup-equivalent; consider adding/confirming a registration-gate test if the suite has one.)
- **Risk — `FakeTimeProvider` continuation timing:** `Advance()` completes the delay but the loop continuation runs asynchronously on the thread pool. The test must await a signal that the sweep was entered before the next advance (e.g. a `TaskCompletionSource` set in the `RunSweepAsync` mock callback) to avoid racing the continuation. This is implementation detail for T3.

## 7. Out of scope

- **Hardening the other timing-based tests** (notably `ExecuteAsync_WhenMultipleCyclesRun_...` at line 138, which uses `AtLeast(2)` over a 250 ms window). Not reported flaky; leave on real time to keep blast radius minimal. Candidate for a future hardening pass if it flakes.
- **Global `TimeProvider` DI registration / Option B.** No precedent; not needed for this fix.
- **Refactoring `RunSweepAsync` cadence or the `SemaphoreSlim` serialization.** Production logic is correct; untouched.
- **No `codegraph`/`docs_explore` index updates required** beyond the plan index; the `code-refs` symbols are unchanged in identity (same class/method names, new overload).

## 8. Required skills

- `dotnet-dev` — production `TimeProvider` injection + delegating overload (T1).
- `test-dev` — test package reference (T2), deterministic `FakeTimeProvider` test rewrite (T3), ctor-null case (T4), verification harness (T5).
- `code-review`, `security-review` — T6.
- `docs-dev` — plan closeout + canonical doc check (T7).

(Skill-to-agent assignment is the orchestrator's responsibility, not this plan's.)

## 9. Verification harness

- **Build:** whole-solution `Release` build, 0 errors / 0 warnings.
- **Unit:** `5-Test/MotorcycleRAG.Application.Tests` passes with 0 failures; in particular the converted `ExecuteAsync_UsesConfiguredOrphanSweepIntervalBetweenCycles` passes.
- **Flake guard:** run the converted test repeatedly (e.g. `dotnet test ... --filter "FullyQualifiedName~ExecuteAsync_UsesConfiguredOrphanSweepIntervalBetweenCycles" -r 50` or loop) with 0 failures.
- **RED gate (optional but recommended):** confirm the new test's assertion fails against the unmodified production `Task.Delay` (no `TimeProvider`) — proving the test genuinely exercises the injection.
- **Reviews:** `code-reviewer` APPROVED; `security-review` PASS (no secret/input-output/trigger surface — pure time abstraction). No Azure read required.
