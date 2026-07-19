---
name: github-devops/build-performance
description: Speed up CI builds — MSBuild parallelism, /graph mode, bottleneck diagnosis, MSBuild Server for sequential CI agents.
source: https://github.com/dotnet/skills/tree/main/plugins/dotnet-msbuild/skills/build-parallelism
       https://github.com/dotnet/skills/tree/main/plugins/dotnet-msbuild/skills/build-perf-diagnostics
       https://github.com/dotnet/skills/tree/main/plugins/dotnet-msbuild/skills/msbuild-server
---

# Build Performance — CI

## Parallelism

### Always Use `-m` in CI

The default is `-m:1` (sequential). Use `-m` without a number to use all logical CPUs on the runner:

```yaml
- name: Build
  run: dotnet build -m /bl:{}

- name: Test
  run: dotnet test -m /bl:{}
```

### Graph Build Mode (`/graph`)

Constructs the full project dependency graph before compilation. Enables better scheduling and avoids redundant evaluations:

```yaml
- name: Build (graph mode)
  run: dotnet build -m /graph /bl:{}
```

**Requirement:** all inter-project dependencies must use `<ProjectReference>` — not `<Reference>` with `HintPath`. Mixed reference styles break graph mode.

**Best for:** large solutions (10+ projects). Small solutions may not see improvement from graph overhead.

---

## Diagnosing Slow CI Builds

### Step 1: Generate a Performance Binlog

Build twice — the second binlog shows the incremental state:

```bash
dotnet build -m /bl:first.binlog
dotnet build -m /bl:second.binlog
```

Replay with performance summary:

```bash
dotnet msbuild second.binlog -noconlog \
  -fl -flp:v=diag;logfile=perf.log;performancesummary
```

### Step 2: Identify Bottlenecks

Key metrics to look for in the performance summary:

| Metric | Threshold | Action |
|---|---|---|
| Node utilization | < 80% | Dependency chain is too serial; split projects or parallelize |
| Single target > 50% of total | — | Split large project or optimize the critical path |
| `ResolveAssemblyReference` > 5s | — | Reduce transitive references; use `DisableTransitiveProjectReferences` |
| Roslyn analyzer time > 30% | — | Disable analyzers in dev/CI builds; enforce only on release |
| NuGet restore running during build | — | Add separate restore step; enable static graph evaluation |

### Step 3: Seven Common Bottleneck Categories

**1. ResolveAssemblyReference (RAR) slowness**

```xml
<!-- Reduce transitive references: add to Directory.Build.props -->
<PropertyGroup>
  <DisableTransitiveProjectReferences>true</DisableTransitiveProjectReferences>
</PropertyGroup>

<!-- Mark build-time-only references as non-output -->
<ProjectReference Include="..\CodeGen\CodeGen.csproj"
                  ReferenceOutputAssembly="false"
                  OutputItemType="Analyzer" />
```

**2. Analyzer overhead**

```xml
<!-- Conditionally disable in debug/CI builds; enforce on release -->
<PropertyGroup Condition="'$(Configuration)' != 'Release'">
  <RunAnalyzers>false</RunAnalyzers>
</PropertyGroup>
```

**3. Excessive File I/O (Copy tasks)**

```xml
<!-- Use hardlinks instead of copies where possible -->
<PropertyGroup>
  <UseHardlinksIfPossible>true</UseHardlinksIfPossible>
</PropertyProperty>
```

**4. NuGet restore during build**

```yaml
# GitHub Actions: separate restore from build
- name: Restore
  run: dotnet restore /p:UseStaticGraphEvaluation=true

- name: Build
  run: dotnet build --no-restore -m /bl:{}
```

**5. Serial dependency chain (deep graph)**

Wide flat graphs (`A → B`, `A → C`, `A → D`) parallelize well. Deep chains (`A → B → C → D`) are bottlenecked by the longest path. Identify and split the critical path project.

**6. Large single project**

If one project dominates build time, split it into smaller libraries with explicit boundaries. This enables parallel compilation of independent parts.

**7. Excessive copy output**

Use centralized output with `.NET 8+ ArtifactsPath`:

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>
</PropertyProperty>
```

---

## MSBuild Server (Sequential CI Agents)

For CI agents that run many sequential builds of the same repo (e.g., PR check + merge check on the same agent), the MSBuild Server caches evaluation results between builds — similar to the advantage Visual Studio gets from its long-lived process.

```yaml
- name: Enable MSBuild Server
  run: echo "MSBUILDUSESERVER=1" >> $GITHUB_ENV

- name: Build (warm server)
  run: dotnet build -m /bl:{}
```

**When it helps:** multiple sequential `dotnet build` calls on the same agent process (self-hosted runners with large Directory.Build chains).

**When it doesn't help:**
- First build on a cold agent (no cache to reuse)
- GitHub-hosted runners that terminate after each job
- When build correctness issues appear — run `dotnet build-server shutdown` to reset

**Validation:**

```bash
# First (cold): server starts
dotnet build
# Second (warm): noticeably faster — measures server benefit
dotnet build
# Confirm server can restart cleanly
dotnet build-server shutdown && dotnet build
```

---

## Quick Wins Checklist

- [ ] Add `-m` to all `dotnet build`/`dotnet test` commands
- [ ] Add a separate `dotnet restore --no-cache` step before build
- [ ] Pass `--no-restore` to `dotnet build` (restore already done)
- [ ] Enable `/graph` if all references use `<ProjectReference>`
- [ ] Set `MSBUILDUSESERVER=1` for self-hosted runners with sequential builds
- [ ] Disable analyzers in non-Release CI configurations
- [ ] Cache NuGet packages between runs (see [Incremental Build](./incremental-build.md))

---

## References

- [MSBuild parallel builds](https://learn.microsoft.com/visualstudio/msbuild/building-multiple-projects-in-parallel-with-msbuild)
- [Static graph evaluation](https://learn.microsoft.com/visualstudio/msbuild/msbuild-graph)
- [MSBuild Server](https://learn.microsoft.com/visualstudio/msbuild/msbuild-server)
