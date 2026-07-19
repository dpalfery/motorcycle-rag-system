---
name: github-devops/msbuild-modernization
description: Migrate legacy .csproj (ToolsVersion, packages.config, explicit file lists) to SDK-style projects with Central Package Management.
source: https://github.com/dotnet/skills/tree/main/plugins/dotnet-msbuild/skills/msbuild-modernization
---

# MSBuild Modernization — Legacy to SDK-Style

## Identifying Legacy vs SDK-Style

**Legacy indicators:**
- `<Project ToolsVersion="15.0" xmlns="...">`
- Explicit `<Compile Include="..." />` for every `.cs` file
- `Properties\AssemblyInfo.cs` with assembly-level attributes
- `packages.config` file present
- File is >50 lines for a simple class library

**SDK-style indicators:**
- `<Project Sdk="Microsoft.NET.Sdk">`
- Minimal content — 10–15 lines for a library
- No explicit file includes (implicit globbing)
- `<PackageReference>` items

---

## Migration Checklist

### Step 1: Replace Project Root Element

```xml
<!-- BEFORE -->
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" ... />
  <!-- ... -->
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>

<!-- AFTER -->
<Project Sdk="Microsoft.NET.Sdk">
  <!-- ... -->
</Project>
```

Remove: XML declaration, `ToolsVersion`, `xmlns`, and both `<Import>` lines. The `Sdk` attribute replaces all of them.

### Step 2: Set TargetFramework

```xml
<!-- BEFORE -->
<TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>

<!-- AFTER -->
<TargetFramework>net472</TargetFramework>
```

| Legacy `TargetFrameworkVersion` | SDK-style `TargetFramework` |
|---|---|
| `v4.6.1` | `net461` |
| `v4.7.2` | `net472` |
| `v4.8` | `net48` |
| Migrating to .NET 8 | `net8.0` |
| Migrating to .NET 10 | `net10.0` |

### Step 3: Remove Explicit File Includes

Delete all `<Compile Include>` and `<Content Include>` item groups — SDK-style projects include them via implicit globbing.

**Exception:** keep explicit entries for files outside the project directory or needing special metadata:

```xml
<ItemGroup>
  <Content Include="..\shared\config.json" Link="config.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

### Step 4: Replace AssemblyInfo.cs

Move assembly attributes into the `.csproj`:

```xml
<PropertyGroup>
  <AssemblyTitle>MotorcycleRAG.Application</AssemblyTitle>
  <Description>Application layer services</Description>
  <Version>1.0.0</Version>
</PropertyGroup>
```

Delete `Properties\AssemblyInfo.cs` — the SDK auto-generates assembly attributes from these properties.

If you want to keep `AssemblyInfo.cs`:

```xml
<PropertyGroup>
  <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
</PropertyProperty>
```

### Step 5: Migrate packages.config → PackageReference

```xml
<!-- BEFORE (packages.config) -->
<package id="Newtonsoft.Json" version="13.0.3" targetFramework="net472" />

<!-- AFTER (.csproj) -->
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
```

Delete `packages.config` after migration. Remove the `<runtime>` binding redirects section from `app.config` — SDK-style projects auto-generate them.

### Step 6: Remove Unnecessary Boilerplate

Delete these — the SDK provides sensible defaults:

```xml
<!-- All of the following can be deleted -->
<Configuration Condition=" '$(Configuration)' == '' ">Debug</Configuration>
<Platform Condition=" '$(Platform)' == '' ">AnyCPU</Platform>
<ProjectGuid>{...}</ProjectGuid>
<OutputType>Library</OutputType>       <!-- keep only if not Library -->
<AppDesignerFolder>Properties</AppDesignerFolder>
<FileAlignment>512</FileAlignment>
<Deterministic>true</Deterministic>
<AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>

<!-- Debug/Release config blocks — SDK defaults match these -->
<PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Debug|AnyCPU'"> ... </PropertyGroup>
<PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Release|AnyCPU'"> ... </PropertyGroup>

<!-- Framework assembly references — implicit in SDK -->
<Reference Include="System" />
<Reference Include="System.Core" />
<Reference Include="System.Data" />
<Reference Include="System.Xml" />
<Reference Include="Microsoft.CSharp" />

<!-- Designer service entries -->
<Service Include="{508349B6-...}" />
```

### Step 7: Enable Modern Features

```xml
<PropertyGroup>
  <TargetFramework>net8.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

**Avoid `<LangVersion>latest`** — the effective language version is determined by the SDK, so builds can silently vary across machines. Either omit `<LangVersion>` (SDK default) or pin an explicit numeric value (`<LangVersion>12</LangVersion>`). Pin the SDK version repo-wide with `global.json` for reproducible builds.

---

## Complete Before/After

```xml
<!-- BEFORE — 65 lines for a simple library -->
<?xml version="1.0" encoding="utf-8"?>
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  ...65 lines...
</Project>

<!-- AFTER — 8 lines for the same library -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
```

---

## Common Migration Issues

**Embedded resources outside project directory:**

```xml
<EmbeddedResource Include="..\shared\Schemas\*.xsd" LinkBase="Schemas" />
```

**Content files with CopyToOutputDirectory:** still need explicit entries:

```xml
<Content Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
```

**Multi-targeting:**

```xml
<TargetFrameworks>net472;net8.0</TargetFrameworks>
```

**WPF/WinForms (.NET 5+):**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

**Test projects:**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

---

## Automation Tools

| Tool | Usage |
|---|---|
| `dotnet try-convert` | `dotnet tool install -g try-convert` — automated first pass |
| .NET Upgrade Assistant | `dotnet tool install -g upgrade-assistant` — full migration including API changes |
| Visual Studio | Right-click `packages.config` → *Migrate packages.config to PackageReference* |

**Recommended workflow:**
1. Run `try-convert` for a first pass
2. Review and clean up the output manually
3. Build and fix any issues
4. Enable `Nullable` and `ImplicitUsings`
5. Consolidate shared settings into `Directory.Build.props`

---

## References

- [MSBuild SDK-style projects](https://learn.microsoft.com/dotnet/core/project-sdk/overview)
- [dotnet try-convert](https://github.com/dotnet/try-convert)
- [.NET Upgrade Assistant](https://learn.microsoft.com/dotnet/core/porting/upgrade-assistant-overview)
