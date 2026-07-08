---
name: github-devops/incremental-build
description: GitHub Actions caching for NuGet/obj, MSBuild incremental build mechanics, diagnosing always-rebuilding targets.
source: https://github.com/dotnet/skills/tree/main/plugins/dotnet-msbuild/skills/incremental-build
---

# Incremental Build & Caching

## GitHub Actions: Cache NuGet Packages

```yaml
- name: Cache NuGet packages
  uses: actions/cache@v4
  with:
    path: ~/.nuget/packages
    key: ${{ runner.os }}-nuget-${{ hashFiles('**/Directory.Packages.props', '**/packages.lock.json') }}
    restore-keys: |
      ${{ runner.os }}-nuget-
```

**Key tips:**
- Hash `Directory.Packages.props` (Central Package Management) or `**/*.csproj` — whichever is your source of truth for versions
- Use `packages.lock.json` (enable with `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`) for deterministic cache keys
- Restore keys (prefix fallback) allow partial cache hits on package subset changes

---

## GitHub Actions: Cache `obj/` Directories (MSBuild Intermediate)

```yaml
- name: Cache MSBuild intermediate outputs
  uses: actions/cache@v4
  with:
    path: |
      **/obj/
      **/bin/
    key: ${{ runner.os }}-msbuild-${{ hashFiles('**/*.csproj', '**/Directory.Build.*') }}
    restore-keys: |
      ${{ runner.os }}-msbuild-
```

**Warning:** `bin/` cache can grow large and is often not worth it. Profile before caching — NuGet packages provide 80% of the benefit at far lower cache sizes.

---

## How MSBuild Incremental Build Works

MSBuild skips a target when all its `Outputs` are newer than all its `Inputs`. Both attributes must be declared:

```xml
<Target Name="GenerateConfig"
        Inputs="$(MSBuildProjectFile);config.template.json"
        Outputs="$(IntermediateOutputPath)config.generated.json">
    <!-- This target is SKIPPED if the output is newer than both inputs -->
</Target>
```

**Targets with no `Inputs`/`Outputs` always run.** The most common cause of slow incremental builds is custom targets missing these attributes.

---

## Eight Causes of Broken Incremental Builds

Diagnose by building twice and comparing the second binlog for "Building target completely" vs "Skipping target":

```bash
dotnet build /bl:first.binlog
dotnet build /bl:second.binlog
# In second log, look for: "Building target completely" when you expected "Skipping target"
```

| Cause | Symptom | Fix |
|---|---|---|
| **Missing Inputs/Outputs** | Target always runs | Add `Inputs` and `Outputs` attributes |
| **Volatile output paths** | Timestamps/GUIDs in paths — new path each run | Use stable paths under `$(IntermediateOutputPath)` |
| **Untracked file writes** | Files created outside declared `Outputs` confuse downstream targets | Register all generated files in `FileWrites` |
| **Missing FileWrites registration** | Generated files accumulate; cleanup removes valid outputs | Add `<ItemGroup><FileWrites Include="..." /></ItemGroup>` |
| **Glob changes** | Adding/removing source files changes `Inputs` item set | Expected behavior — cannot avoid |
| **Property changes** | Configuration or TFM switch triggers full rebuild | Expected behavior |
| **NuGet updates** | Package version changes invalidate assembly paths | Expected — update lockfile |
| **Compiler cache invalidation** | Roslyn VBCSCompiler recycled between builds | Use MSBuild Server to keep compiler alive |

---

## Well-Structured Incremental Target (Example)

```xml
<Target Name="GenerateApiClient"
        BeforeTargets="CoreCompile"
        Inputs="$(MSBuildProjectFile);openapi.json"
        Outputs="$(IntermediateOutputPath)GeneratedApiClient.cs">

    <Exec Command="dotnet tool run openapi-generator -- ..." />

    <!-- Register the output so MSBuild tracks it for cleanup and incremental logic -->
    <ItemGroup>
        <FileWrites Include="$(IntermediateOutputPath)GeneratedApiClient.cs" />
        <Compile Include="$(IntermediateOutputPath)GeneratedApiClient.cs" />
    </ItemGroup>
</Target>
```

**Pattern:**
- `$(MSBuildProjectFile)` in `Inputs` — rebuilds when the project itself changes
- `$(IntermediateOutputPath)` for generated files — stable path, not random
- `FileWrites` registration — enables `Clean` target and downstream tracking
- `BeforeTargets="CoreCompile"` — generated file available to the compiler

---

## Incremental Build Anti-patterns

```xml
<!-- ANTI-PATTERN: timestamp in output path — always "new" file, always rebuilds -->
<Target Name="GenerateVersion"
        Outputs="$(IntermediateOutputPath)version-$(NOW).cs">

<!-- ANTI-PATTERN: no Inputs/Outputs — always runs, blocks downstream incremental -->
<Target Name="CopyAssets" AfterTargets="Build">
    <Copy SourceFiles="@(Assets)" DestinationFolder="$(OutputPath)" />
</Target>

<!-- CORRECT: declare Inputs and Outputs so Copy can be skipped -->
<Target Name="CopyAssets" AfterTargets="Build"
        Inputs="@(Assets)"
        Outputs="@(Assets->'$(OutputPath)%(Filename)%(Extension)')">
    <Copy SourceFiles="@(Assets)" DestinationFolder="$(OutputPath)"
          SkipUnchangedFiles="true" />
</Target>
```

---

## `packages.lock.json` for Deterministic CI Restores

Enable lockfile-based restore to get reproducible, cache-friendly NuGet restores:

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  <!-- Fail if lockfile is out of date in CI -->
  <RestoreLockedMode Condition="'$(CI)' == 'true'">true</RestoreLockedMode>
</PropertyGroup>
```

Commit `packages.lock.json` to the repository. CI will fail fast if the lockfile doesn't match the declared packages — preventing silent package drift.

---

## References

- [MSBuild incremental builds](https://learn.microsoft.com/visualstudio/msbuild/incremental-builds)
- [GitHub Actions cache](https://docs.github.com/en/actions/writing-workflows/choosing-what-your-workflow-does/caching-dependencies-to-speed-up-workflows)
- [NuGet package lock files](https://learn.microsoft.com/nuget/consume-packages/package-references-in-project-files#locking-dependencies)
