---
name: app-docs-standard
description: Standardizes application-level documentation structure (Overview, Onboarding, Architecture, Requirements). Use when creating or updating documentation for a specific application in this repository.
metadata:
  version: 1.0.0
---

# Application Documentation Standard

When creating or updating documentation for an application in this repository, you must follow this standard structure.

## Structure

For each application, documentation is split between the code root and a dedicated folder in `6-Docs/`.

1. **Overview Document**: Located at the root of the application's code folder (e.g., `src/MyApp/README.md` or `src/MyApp/Overview.md`). This provides a high-level overview of the application.
2. **Dedicated Documentation Folder**: Located in `6-Docs/{app-name}/`. This folder contains detailed application documentation.

### Dedicated Documentation Folder Contents (`6-Docs/{app-name}/`)

The dedicated folder must contain the following documents:

#### 1. Developer Onboarding (`onboarding.md`)
Describes how to get the application running.
- **Audience:** Human developers and LLM-based agents.
- **Goal:** Keep it concise but do not skip steps.
- **Contents:**
  - All developer environment and runtime dependencies needed.
  - How to debug the application.
  - Any non-standard procedures for this application.

#### 2. Architecture Document (`architecture.md`)
Follows the outline and standard set in the `product-owner` skill's Design Phase.
- **Contents:**
  - **Overview** — what is being built and the shape of the solution.
  - **Architecture** — high-level structure; Mermaid diagram(s) where useful.
  - **Components and Interfaces** — the parts and the contracts between them.
  - **Data Models** — entities, fields, relationships, schemas.
  - **Error Handling** — failure modes and how the system responds.
  - **Testing Strategy** — how the design will be validated (unit, integration, e2e).

#### 3. Requirements Document (`requirements.md`)
Follows the outline and standard set in the `product-owner` skill's Requirements Phase.
- **Contents:**
  - **Introduction** — Short paragraph summarizing the feature/app and its purpose.
  - **Requirements** — A hierarchical, numbered list of requirements. Each requirement has one user story and a numbered list of acceptance criteria written in EARS (Easy Approach to Requirements Syntax): WHEN/IF [trigger or precondition] THEN [system] SHALL [response].
