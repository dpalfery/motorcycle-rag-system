---
name: github-devops/msbuild-antipatterns
description: 15 MSBuild anti-patterns that break CI builds — detection and remediation for cross-platform failures, hardcoded paths, shell commands, and more.
source: https://github.com/dotnet/skills/tree/main/plugins/dotnet-msbuild/skills/msbuild-antipatterns
---

# MSBuild Anti-patterns Catalog

15 patterns that break CI builds or cause cross-platform failures, with detection guidance and fixes.

---

## AP-01: `<Exec>` Instead of Built-in Tasks

**Problem:** `<Exec Command="mkdir ..." />` or `<Exec Command="copy ..." />` — shell commands are OS-specific and fail on Linux CI runners.

**Fix:** Use built-in MSBuild tasks that are cross-platform and support incremental build:

```xml
<!-- WRONG -->
<Exec Command="mkdir $(OutputPath)" />
<Exec Command="xcopy /Y src dest" />
<Exec Command="del *.tmp" />

<!-- CORRECT -->
<MakeDir Directories="$(OutputPath)" />
<Copy SourceFiles="@(Sources)" DestinationFolder="$(Dest)" />
<Delete Files="@(TempFiles)" />
```

Other built-in tasks: `<Move>`, `<WriteLinesToFile>`, `<ReadLinesFromFile>`, `<Touch>`, `<Unzip>`, `<ZipDirectory>`.

---

## AP-02: Unquoted Conditions

**Problem:** Conditions fail when property values contain spaces or are empty.

```xml
<!-- WRONG — fails when $(Configuration) is empty or contains spaces -->
<PropertyGroup Condition="$(Configuration) == Release">

<!-- CORRECT — always quote both sides -->
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
```

**Rule:** Always quote both sides of comparisons in `Condition` attributes.

---

## AP-03: Hardcoded Absolute Paths

**Problem:** `C:\tools\` paths break on every other developer machine and all CI Linux runners.

```xml
<!-- WRONG -->
<Import Project="C:\tools\build\Common.props" />
<Exec Command="C:\Program Files\MyTool\tool.exe" />

<!-- CORRECT — relative to the current file -->
<Import Project="$(MSBuildThisFileDirectory)..\..\build\Common.props" />
<Exec Command="$(MSBuildProjectDirectory)\tools\mytool$(ExeExtension)" />
```

Useful path properties:
- `$(MSBuildThisFileDirectory)` — directory of the current `.props`/`.targets` file
- `$(MSBuildProjectDirectory)` — directory of the `.csproj` being built
- `$(ExeExtension)` — `.exe` on Windows, empty on Linux/macOS

---

## AP-04: Restating SDK Defaults

**Problem:** Duplicating properties the SDK already sets creates noise and can mask intentional overrides later.

```xml
<!-- WRONG — SDK already provides these defaults -->
<PropertyGroup>
  <Configuration Condition="'$(Configuration)' == ''">Debug</Configuration>
  <Platform Condition="'$(Platform)' == ''">AnyCPU</Platform>
  <OutputType>Library</OutputType>
  <Deterministic>true</Deterministic>
  <DebugType>pdbonly</DebugType>
</PropertyGroup>
```

Delete any property that matches the SDK default. Run `dotnet msbuild -pp:output.xml` to see what the SDK already sets.

---

## AP-05: Manual File Listing in SDK Projects

**Problem:** Explicit `<Compile Include>` entries in SDK-style projects override the implicit glob — file additions/removals are not picked up automatically.

```xml
<!-- WRONG — explicit list in SDK project; will miss new files -->
<ItemGroup>
  <Compile Include="Services\SearchService.cs" />
  <Compile Include="Services\IngestionService.cs" />
</ItemGroup>

<!-- CORRECT — implicit glob picks up all .cs files automatically -->
<!-- (no ItemGroup needed) -->
```

Only add explicit `<Compile>` entries to *exclude* files or add files outside the project directory.

---

## AP-06: Legacy NuGet References (`<Reference>` with HintPath)

**Problem:** `packages.config`-style references don't support transitive dependencies or Central Package Management.

```xml
<!-- WRONG — legacy HintPath reference -->
<Reference Include="Newtonsoft.Json">
  <HintPath>..\packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll</HintPath>
</Reference>

<!-- CORRECT — PackageReference with transitive support -->
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
```

---

## AP-07: Missing PrivateAssets on Analyzers and Tools

**Problem:** Analyzer and tool packages leaked to consumers via transitive dependencies — consumers get unwanted build tools in their dependency graph.

```xml
<!-- WRONG — analyzer leaks to consumers -->
<PackageReference Include="StyleCop.Analyzers" Version="1.2.0" />

<!-- CORRECT — private to this project only -->
<PackageReference Include="StyleCop.Analyzers" Version="1.2.0"
                  PrivateAssets="all" />
```

Always set `PrivateAssets="all"` on: analyzers, source generators, CLI tools, test adapters.

---

## AP-08: Targets Without Import Guards

**Problem:** Importing the same `.targets` file multiple times causes duplicate target definitions.

```xml
<!-- WRONG — no guard; breaks if imported twice -->
<Project>
  <Target Name="ValidateBuild" BeforeTargets="Build">
    ...
  </Target>
</Project>

<!-- CORRECT — guard against double import -->
<Project>
  <PropertyGroup>
    <MyTargetsImported Condition="'$(MyTargetsImported)' == ''">true</MyTargetsImported>
  </PropertyGroup>

  <Target Name="ValidateBuild" BeforeTargets="Build"
          Condition="'$(MyTargetsImported)' == 'true'">
    ...
  </Target>
</Project>
```

Or use `Condition="'$(MyCustomTargets_Imported)' == ''"` wrapping the entire file content.

---

## AP-09: Path Separators (Backslash on Linux)

**Problem:** Hardcoded `\` path separators fail on Linux/macOS CI.

```xml
<!-- WRONG -->
<PropertyGroup>
  <OutputDir>$(MSBuildProjectDirectory)\bin\custom\</OutputDir>
</PropertyProperty>

<!-- CORRECT — use forward slash (works on all OSes) or $([System.IO.Path]::Combine()) -->
<PropertyGroup>
  <OutputDir>$(MSBuildProjectDirectory)/bin/custom/</OutputDir>
</PropertyGroup>
```

MSBuild normalizes forward slashes on Windows. Use `/` everywhere in MSBuild files.

---

## AP-10: Property Override Scoping

**Problem:** Setting a global property in a `.targets` file that projects should be able to override — but `.targets` runs after the project, so it silently wins.

```xml
<!-- WRONG in Directory.Build.targets — projects cannot override this -->
<PropertyGroup>
  <OutputPath>$(RepoRoot)artifacts\</OutputPath>
</PropertyGroup>

<!-- CORRECT in Directory.Build.props — projects can override -->
<PropertyGroup>
  <OutputPath>$(RepoRoot)artifacts\</OutputPath>
</PropertyGroup>
```

Use `.props` for defaults projects can override. Use `.targets` only for values that must be final.

---

## AP-11: `BeforeTargets`/`AfterTargets` on Core SDK Targets

**Problem:** Hooking into internal SDK targets (`_GenerateDepsFile`, `_CopyFilesMarkedCopyLocal`) — these are implementation details that change between SDK versions, breaking builds on SDK upgrade.

```xml
<!-- WRONG — internal target names are unstable -->
<Target Name="MyStep" AfterTargets="_CopyFilesMarkedCopyLocal">

<!-- CORRECT — hook into public SDK extension points -->
<Target Name="MyStep" AfterTargets="Build">
<Target Name="MyStep" BeforeTargets="Publish">
<Target Name="MyStep" AfterTargets="Pack">
```

Public extension points: `Build`, `Rebuild`, `Clean`, `Test`, `Publish`, `Pack`, `Restore`.

---

## AP-12: Mutable Item Metadata in Conditions

**Problem:** Using `%(Filename)` or other item metadata in property conditions — metadata is not available in property evaluation context.

```xml
<!-- WRONG — %(Filename) is item metadata, not available in PropertyGroup conditions -->
<PropertyGroup Condition="'%(Compile.Filename)' == 'Program'">

<!-- CORRECT — use item conditions or Target with batching -->
<Target Name="CheckFiles">
  <Message Text="Found: %(Compile.Filename)" Importance="High" />
</Target>
```

---

## AP-13: Missing `$(Configuration)` Conditions on Expensive Steps

**Problem:** Heavy build steps (code generation, documentation, packaging) run in debug builds and slow down local iteration.

```xml
<!-- WRONG — always runs, slows debug builds -->
<Target Name="GenerateDocs" AfterTargets="Build">
  <Exec Command="docgen.exe" />
</Target>

<!-- CORRECT — only in Release/CI -->
<Target Name="GenerateDocs" AfterTargets="Build"
        Condition="'$(Configuration)' == 'Release'">
  <Exec Command="docgen.exe" />
</Target>
```

---

## AP-14: Ignoring `$(CI)` Environment Variable

**Problem:** Steps that are harmless locally (prompts, GUI tool launches) break in CI where there's no interactive terminal.

```xml
<!-- CORRECT — check for CI environment -->
<Target Name="ShowReport" AfterTargets="Test"
        Condition="'$(CI)' != 'true'">
  <Exec Command="start report.html" />  <!-- only opens browser locally -->
</Target>
```

GitHub Actions, Azure DevOps, and most CI systems set `CI=true` automatically.

---

## AP-15: Non-Deterministic Builds

**Problem:** Timestamps, random GUIDs, or machine-specific paths embedded in outputs make builds non-reproducible — same inputs produce different outputs on different machines/runs.

```xml
<!-- WRONG — timestamp in assembly version makes output non-deterministic -->
<PropertyGroup>
  <AssemblyVersion>1.0.$(BuildTimestamp)</AssemblyVersion>
</PropertyGroup>

<!-- CORRECT — use build number from CI environment; keep version deterministic locally -->
<PropertyGroup>
  <AssemblyVersion Condition="'$(BUILD_NUMBER)' != ''">
    1.0.$(BUILD_NUMBER)
  </AssemblyVersion>
  <AssemblyVersion Condition="'$(BUILD_NUMBER)' == ''">1.0.0</AssemblyVersion>

  <!-- Always enable Roslyn's deterministic output -->
  <Deterministic>true</Deterministic>
</PropertyGroup>
```

`<Deterministic>true</Deterministic>` is already set by the SDK — confirm it isn't being overridden to `false`.

---

## References

- [MSBuild common items](https://learn.microsoft.com/visualstudio/msbuild/common-msbuild-project-items)
- [MSBuild conditions](https://learn.microsoft.com/visualstudio/msbuild/msbuild-conditions)
- [Reproducible builds](https://learn.microsoft.com/dotnet/core/deploying/reproducible-builds)
