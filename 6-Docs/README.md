---
id: system/docs-index
title: MotorcycleRAG Documentation
doc-type: index
status: current
owner: Maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# MotorcycleRAG Documentation

This directory is the canonical home for detailed MotorcycleRAG documentation. Start with the system documents, then follow the component or operational path that matches your task.

## System

- [Developer onboarding](system/onboarding.md)
- [System architecture](system/architecture.md)
- [System requirements](system/requirements.md)
- [Documentation standard](documentation-standard.md)
- [Documentation ontology](documentation-ontology.md)
- [Component catalog](catalog.md)

## Applications and services

- [API](MotorcycleRAG.API/)
- [Admin Desktop](MotorcycleRAG.AdminDesktop/)
- [Mobile App](MotorcycleRAG.MobileApp/)
- [Web UI](MotorcycleRag.WebUI/)
- [Web UI BFF](MotorcycleRag.WebUI.BFF/)
- [Local Processing Service](local-processing-service/)
- [Azure Environment](AzureEnvironment/)

## Build, deployment, and operations

- [DevOps documentation](DevOps/)
- [Operations runbooks](operations/) — [GitHub branch protection](operations/github-branch-protection.md); BFF data protection: [operations](operations/webui-bff-data-protection-operations.md), [disaster recovery](operations/webui-bff-data-protection-disaster-recovery.md), [troubleshooting](operations/webui-bff-data-protection-troubleshooting.md)
- [Reference material](reference/)
- [Architecture placement rules](rules/architecture-general.md)
- [Architecture decisions](adr/)
- [Agent governance](system/agent-governance.md)
- [Security directives](system/security.md)

## Change history

- [Plan index and active plans](plans/README.md)
- [Specification index and active specifications](specs/README.md)
- [Archived material](archive/) — historical reference only; never treat it as current guidance.

## Finding documentation

Use the [catalog](catalog.md) to locate the overview README, detailed documentation, source root, and owner for each maintained component. Root-level application READMEs provide short overviews and link back here; detailed content belongs in this directory unless GitHub requires a standard repository file.
