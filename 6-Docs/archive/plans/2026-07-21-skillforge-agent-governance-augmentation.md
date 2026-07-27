# 2026-07-21 SkillForge Upstream Augmentation Plan: Multi-Harness Agent & Skill Governance

**Status:** Archived  
**Date:** 2026-07-21  

## Goal

Augment the core **SkillForge** toolkit ([7-Deployment/tools/SkillForge/](file:///Users/dave/git/motorcycle-rag-system/7-Deployment/tools/SkillForge)) with **Agent Harness Governance** capabilities. This enables SkillForge to parse, validate, lint, security-scan, and routing-test both **Agent Skills** (`SKILL.md`) and **Agent Definitions** across diverse AI coding harnesses (`.codex/`, `.cursor/`, `.claude/`, `.github/`, `.opencode/`, `.kilo/`).

The design is general-purpose, backwards-compatible, and structured so that it can be submitted as a Pull Request to the upstream [bonaniibm/SkillForge](https://github.com/bonaniibm/SkillForge) repository.

---

## User Review Required

> [!IMPORTANT]
> **Harness Capability Matrix & Dual Agent/Skill Mapping**
> 
> Not all AI coding harnesses support parent orchestrator agents in their native `agents/` directories.
> - **Native Harness Agents**: Tools like Kilo (`.kilo/agents/`), OpenCode (`.opencode/agents/`), and Codex (`.codex/agents/`) support parent/sub-agents directly in `agents/`.
> - **Skill-Mapped Agents**: Tools like Claude Code (`.claude/`), Cursor (`.cursor/`), and Antigravity invoke parent orchestrators (like `conductor`) via an Agent Skill (e.g. `.agents/skills/conductor/SKILL.md`) rather than an agent manifest in `.claude/agents/`.
> 
> **Design Decision**: SkillForge's `AgentSyncLinter` will use a **Role Satisfaction Engine** driven by a `HarnessCapabilityProfile` registry. A role (e.g., `conductor`) is satisfied for a given harness if it exists either as a native harness agent manifest OR as a registered skill mapping, preventing false-positive "missing agent file" errors.

> [!NOTE]
> **Upstream PR Design Goals**
> - Keep `SkillForge.Core` backwards-compatible: existing skill validation (`skillforge validate`, `skillforge lint`, etc.) remains unchanged.
> - Add a clean `Agent` domain namespace (`SkillForge.Core.Agents`) and CLI command set (`skillforge agent ...`) or unified arguments (`skillforge lint --agents`).
> - Provide configuration via `.skillforge.yml` or `skillforge-config.json` to allow repos to declare custom harness aliases and role satisfaction mappings.

---

## Proposed Changes

### 1. Core Model & Parsing (`SkillForge.Core`)

#### [NEW] [AgentModel.cs](file:///Users/dave/git/motorcycle-rag-system/7-Deployment/tools/SkillForge/src/SkillForge.Core/Agents/Model/AgentModel.cs)
- Defines the unified in-memory representation of an Agent definition:
  - `RoleName` (e.g., `dotnet-dev`, `conductor`, `code-reviewer`)
  - `Harness` (enum `HarnessKind`: `Codex`, `Cursor`, `Claude`, `GitHubCopilot`, `OpenCode`, `Kilo`, `Custom`)
  - `FilePath` & `Format` (`Toml`, `MarkdownFrontmatter`, `Json`)
  - `Description` / `Triggers`
  - `InstructionsBody` (System Prompt)
  - `ModelPreference` & `Tools`

#### [NEW] [AgentSet.cs](file:///Users/dave/git/motorcycle-rag-system/7-Deployment/tools/SkillForge/src/SkillForge.Core/Agents/Model/AgentSet.cs)
- Collection of parsed agents across discovered harness folders, providing matrix lookup (`GetRoleHarnessMatrix()`).

#### [NEW] [HarnessCapabilityProfile.cs](file:///Users/dave/git/motorcycle-rag-system/7-Deployment/tools/SkillForge/src/SkillForge.Core/Agents/Model/HarnessCapabilityProfile.cs)
- Configurable harness capability model:
  - Supports native agent manifests in `agents/`? (True/False)
  - Mapped role overrides (e.g., `conductor` mapped to `skill:conductor` for Claude/Cursor/Antigravity).

#### [NEW] [IAgentParser.cs](file:///Users/dave/git/motorcycle-rag-system/7-Deployment/tools/SkillForge/src/SkillForge.Core/Agents/Parsing/IAgentParser.cs) & Implementations
- `TomlAgentParser`: Parses `.codex/agents/*.toml` using TOML reader.
- `MarkdownAgentParser`: Parses `.cursor/agents/*.agent.md`, `.claude/agents/*.md`, `.github/agents/*.md`, `.opencode/agents/*.md`, `.kilo/agents/*.md`.
- `AgentLoader`: Discovers all 6 harness agent directories and skill mappings.

---

### 2. Validation & Synchronization Engine (`SkillForge.Core`)

#### [NEW] [AgentSpecValidator.cs](file:///Users/dave/git/motorcycle-rag-system/7-Deployment/tools/SkillForge/src/SkillForge.Core/Agents/Validation/AgentSpecValidator.cs)
- Validates individual agent files against spec rules:
  - `SF-AGENT-SPEC-001`: Invalid or missing role name.
  - `SF-AGENT-SPEC-002`: Missing description / routing trigger.
  - `SF-AGENT-SPEC-003`: Empty system prompt / instructions.
  - `SF-AGENT-SPEC-004`: Broken file / rule references.

#### [NEW] [AgentSyncLinter.cs](file:///Users/dave/git/motorcycle-rag-system/7-Deployment/tools/SkillForge/src/SkillForge.Core/Agents/Validation/AgentSyncLinter.cs)
- Evaluates cross-harness role parity and instruction drift:
  - `SF-AGENT-SYNC-001` (Unsatisfied Role): Flags roles missing across expected harnesses, evaluating both native agent manifests and skill mappings via `HarnessCapabilityProfile`.
  - `SF-AGENT-SYNC-002` (Instruction Drift): Uses `TextVectorizer` cosine similarity to detect instruction divergence between harness versions of the same role (e.g., `.codex/agents/dotnet-dev.toml` vs `.claude/agents/dotnet-dev.md`).
  - `SF-AGENT-LINT-001` (Description Score): Runs `DescriptionScorer` against agent triggers (`Use when...`, `Do NOT use for...`).

#### [NEW] [AgentPromptScanner.cs](file:///Users/dave/git/motorcycle-rag-system/7-Deployment/tools/SkillForge/src/SkillForge.Core/Agents/Security/AgentPromptScanner.cs)
- Security scans agent system prompts for prompt injection risks, credential leaks, or safety control bypasses (`SF-AGENT-SEC-xxx`).

#### [NEW] [AgentRoutingEvaluator.cs](file:///Users/dave/git/motorcycle-rag-system/7-Deployment/tools/SkillForge/src/SkillForge.Core/Agents/Routing/AgentRoutingEvaluator.cs)
- Simulates subagent dispatch for prompt eval sets (`agent-routing-tests.yml`) to predict which agent triggers for a given user prompt.

---

### 3. CLI Commands (`SkillForge.Cli`)

#### [NEW] [AgentCommandGroup](file:///Users/dave/git/motorcycle-rag-system/7-Deployment/tools/SkillForge/src/SkillForge.Cli/Commands/Agents/)
Add Spectre.Console CLI commands under the `agent` root command:
- `skillforge agent validate [path]`: Validate agent manifest formatting and links.
- `skillforge agent sync-check`: Verify 6-harness role parity & detect instruction drift.
- `skillforge agent lint`: Score agent description routing metadata and check for trigger overlap.
- `skillforge agent scan`: Scan agent prompts for security vulnerabilities (SARIF/Table/JSON).
- `skillforge agent route --eval <file>`: Run routing regression tests.
- `skillforge agent catalog`: Output the 17-role × 6-harness governance matrix table.
- `skillforge agent new <name>`: Scaffold synchronized agent manifests across all harnesses.

---

### 4. Test Suite (`SkillForge.Tests`)

#### [NEW] [AgentParserTests.cs](file:///Users/dave/git/motorcycle-rag-system/7-Deployment/tools/SkillForge/tests/SkillForge.Tests/AgentParserTests.cs)
- Unit tests for parsing TOML (`.codex`), Frontmatter Markdown (`.cursor`, `.claude`, `.github`, `.opencode`, `.kilo`).

#### [NEW] [AgentSyncLinterTests.cs](file:///Users/dave/git/motorcycle-rag-system/7-Deployment/tools/SkillForge/tests/SkillForge.Tests/AgentSyncLinterTests.cs)
- Tests role satisfaction verification across harnesses, including `conductor` (mapped as a skill in Claude/Cursor, native agent in Kilo/OpenCode).
- Tests instruction drift detection using cosine similarity.

---

## Verification Plan

### Automated Tests
- Build `SkillForge.sln`: `dotnet build 7-Deployment/tools/SkillForge/SkillForge.sln`
- Run xUnit test suite: `dotnet test 7-Deployment/tools/SkillForge/tests/SkillForge.Tests/SkillForge.Tests.csproj`

### Integration Verification
1. Run `skillforge agent catalog` against the repository to view the full 17-role × 6-harness matrix.
2. Run `skillforge agent sync-check` to verify that `conductor` is correctly resolved as satisfied via skill mapping for Claude/Cursor/Antigravity and native agent for Kilo/OpenCode.
3. Run `skillforge agent scan --format sarif` to verify SARIF generation.
4. Run `skillforge agent route --eval ./5-Test/agent-routing-tests.yml` to verify prompt dispatch simulation.
