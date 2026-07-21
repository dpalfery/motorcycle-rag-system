---
id: system/onboarding
title: MotorcycleRAG System Developer Onboarding
doc-type: onboarding
status: current
component: MotorcycleRAG system
source-root: .
owner: Maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# MotorcycleRAG System Developer Onboarding

## Purpose

This guide helps contributors identify the correct application or layer before starting work. MotorcycleRAG is a multi-application system: a .NET API, React/Tauri Admin Desktop, React/BFF Web UI, .NET MAUI Mobile App, and a Python local-processing service.

## Prerequisites

- Git, .NET 10 SDK, Node.js/npm, Python, and Rust/Tauri tooling for the components you intend to run.
- The target application's documented dependencies, local services, and approved configuration.
- An authenticated development identity for read-only cloud diagnosis when needed. Do not place secrets in source-controlled configuration.

## Start a component

1. Read the [component catalog](../catalog.md) and choose the component that owns the behavior.
2. Read its source-root README and detailed `onboarding.md` before installing dependencies.
3. Run the component's focused build/test commands before attempting a system-wide build.
4. For local-first ingestion, start Admin Desktop and the Local Processing Service with their documented health prerequisites; do not bypass the watch-folder contract.
5. Use the API, BFF, and deployment documentation for the exact boundary you are testing.

## Debugging

- Begin with the smallest owning component and its health/diagnostic path.
- Correlate cross-application work with the identifiers exposed by the API and local processor instead of inferring state from the UI.
- For architecture, configuration, or ownership questions, follow the canonical links in the [documentation index](../README.md) rather than archived material.

## Non-standard procedures

Infrastructure and deployments are performed only through GitHub Actions. Local commands may validate or preview configuration when the component documentation permits them, but must not provision cloud resources, push images, or deploy applications directly.
