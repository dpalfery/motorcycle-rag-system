---
name: github-devops/directory-build-organization
description: Centralize MSBuild settings — Directory.Build.props/targets, Directory.Packages.props, Directory.Build.rsp, and multi-level repo hierarchies.
source: https://github.com/dotnet/skills/tree/main/plugins/dotnet-msbuild/skills/directory-build-organization
---

# Directory.Build Organization

## Evaluation Order

```
Directory.Build.props  →  SDK .props  →  YourProject.csproj  →  SDK .targets  →  Directory.Build.targets
```

| File | Use for |
|---|---|
| `Directory.Build.props` | Property defaults, analyzer packages, metadata, settings projects can override |
| `Directory.Build.targets` | Custom build targets, late-bound property overrides, post-build steps |
| `Directory.Packages.props` | Central NuGet package versions (CPM) |
| `Directory.Build.rsp` | Default MSBuild CLI args (applied to all builds in the tree) |

**Rule of thumb:** Properties and items → `.props`. Targets and late logic → `.targets`.

---

## Critical Pitfall: `$(TargetFramework)` in `.props`

`$(TargetFramework)` is **empty** during `.props` evaluation for single-targeting projects — property conditions on it silently fail:

```xml
<!-- WRONG: TargetFramework is empty here for single-TFM projects -->
<Directory.Build.props>
  <PropertyGroup Condition="'$(TargetFramework)' == 'net8.0'">
    <SomeProperty>value</SomeProperty>
  </PropertyGroup>
</Directory.Build.props>

<!-- CORRECT: move TFM conditions to .targets (evaluated after SDK) -->
<Directory.Build.targets>
  <PropertyGroup Condition="'$(TargetFramework)' == 'net8.0'">
    <SomeProperty>value</SomeProperty>
  </PropertyGroup>
</Directory.Build.targets>
```

`ItemGroup` and `Target` conditions are not affected — only `PropertyGroup` conditions using `$(TargetFramework)` in `.props`.

---

## Directory.Build.props — Repo-Wide Defaults

```xml
<Project>
  <PropertyGroup>
    <!-- Language features -->
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>

    <!-- Assembly metadata (avoids duplicating across all .csproj files) -->
    <Company>MotorcycleRAG</Company>
    <Authors>David R Palfery</Authors>
    <Copyright>Copyright © MotorcycleRAG 2025</Copyright>
  </PropertyGroup>

  <!-- Analyzers applied to all projects -->
  <ItemGroup>
    <GlobalPackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" />
  </ItemGroup>
</Project>
```

---

## Directory.Build.targets — Custom Build Logic

```xml
<Project>
  <!-- Validation target — runs for all projects before build -->
  <Target Name="ValidateProjectSettings" BeforeTargets="Build">
    <Error Text="Libraries must not target net472 — use net8.0+"
           Condition="'$(OutputType)' == 'Library' AND '$(TargetFramework)' == 'net472'" />
  </Target>

  <!-- Late-bound property: OutputPath is set by SDK, so safe to use here -->
  <PropertyGroup>
    <DocumentationFile Condition="'$(IsPackable)' == 'true'">
      $(OutputPath)$(AssemblyName).xml
    </DocumentationFile>
  </PropertyGroup>
</Project>
```

---

## Directory.Packages.props — Central Package Management

Single source of truth for all NuGet versions across the repo:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <!-- Application dependencies -->
    <PackageVersion Include="Dapper" Version="2.1.35" />
    <PackageVersion Include="Azure.AI.OpenAI" Version="2.*" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="9.*" />

    <!-- Test dependencies -->
    <PackageVersion Include="xunit" Version="2.9.0" />
    <PackageVersion Include="NSubstitute" Version="5.3.0" />
    <PackageVersion Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>

  <!-- GlobalPackageReference: applied to ALL projects automatically -->
  <ItemGroup>
    <GlobalPackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="9.*" />
  </ItemGroup>
</Project>
```

Projects then omit `Version`:

```xml
<PackageReference Include="Dapper" />   <!-- version comes from Directory.Packages.props -->
```

---

## Directory.Build.rsp — Shared CLI Defaults

```
/maxcpucount
/nodeReuse:false
/consoleLoggerParameters:Summary;ForceNoAlign
/warnAsMessage:MSB3277
```

Applied automatically to all `dotnet`/`msbuild` commands run under the directory. Each argument on its own line. Works with both `msbuild` and `dotnet` CLI in .NET 8+.

---

## Multi-Level Hierarchy

MSBuild only auto-imports the **first** `Directory.Build.props` found walking up. To chain levels, explicitly import the parent at the top of the inner file:

```xml
<!-- src/Directory.Build.props — imports root then adds src-specific settings -->
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))"
          Condition="Exists('$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))')" />

  <PropertyGroup>
    <IsPackable>true</IsPackable>
  </PropertyGroup>
</Project>
```

**Recommended repo layout:**

```
repo/
  Directory.Build.props          ← repo-wide: lang version, company info, analyzers
  Directory.Build.targets        ← repo-wide: custom targets, validation
  Directory.Packages.props       ← all NuGet versions
  Directory.Build.rsp            ← shared CLI defaults (-m, logging)
  src/
    Directory.Build.props        ← imports root; IsPackable=true
  test/
    Directory.Build.props        ← imports root; IsPackable=false; test framework refs
```

---

## .NET 8+ Artifacts Output Layout

Set `ArtifactsPath` to consolidate all build output under one directory (eliminates `bin/obj` clutter per project):

```xml
<!-- Directory.Build.props -->
<PropertyGroup>
  <ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>
</PropertyProperty>
```

Output lands at `artifacts/{project-name}/bin/{config}/{tfm}/` — easier to clean and cache in CI.

Add to `.gitignore`: `artifacts/`

---

## Migration Workflow

1. **Audit** — catalog repeated properties/items across all `.csproj` files
2. **Create root `Directory.Build.props`** — move shared property defaults
3. **Create root `Directory.Build.targets`** — move custom targets and late-bound logic
4. **Create `Directory.Packages.props`** — enable CPM; remove `Version=` from all `PackageReference`
5. **Create `src/` and `test/` inner files** — chain with `GetPathOfFileAbove`
6. **Simplify `.csproj` files** — remove everything that moved to shared files
7. **Validate** — `dotnet restore && dotnet build`; inspect merged view with `dotnet msbuild -pp:output.xml <project>`

---

## Troubleshooting

| Problem | Fix |
|---|---|
| File not picked up on Linux/macOS | Verify exact casing: `Directory.Build.props` (capital D, capital B) |
| Property from `.props` ignored by project | Project overrides it — move to `.targets` |
| Multi-level import not working | Missing `GetPathOfFileAbove` import in inner file |
| `$(TargetFramework)` condition always false in `.props` | Move to `.targets` |
| CPM — package not found | Confirm `Directory.Packages.props` is at/above project; exact filename required |

**Diagnostic:** `dotnet msbuild -pp:output.xml MyProject.csproj` — expands all imports inline to show final property values.

---

## References

- [Directory.Build.props](https://learn.microsoft.com/visualstudio/msbuild/customize-by-directory)
- [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management)
- [.NET Artifacts output layout](https://learn.microsoft.com/dotnet/core/sdk/artifacts-output)
