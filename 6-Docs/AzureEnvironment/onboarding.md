---
id: azure/onboarding
title: Azure Environment Developer Onboarding
doc-type: onboarding
status: current
component: Azure Environment
source-root: 7-Deployment/infrastructure
owner: Platform maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# Azure Environment Developer Onboarding

## Prerequisites

- The repository's infrastructure source under `7-Deployment/infrastructure`.
- Pulumi CLI and Azure CLI for approved read-only diagnostics or local `pulumi preview` validation.
- Access to the relevant GitHub Actions workflow and environment configuration. Secrets remain in the approved GitHub/Azure configuration stores.

## Validate and release

1. Read the infrastructure agent instructions and deployment workflow before changing infrastructure.
2. Update the declarative Pulumi source and run the permitted validation or preview commands.
3. Submit the change through the normal review process.
4. GitHub Actions is the only approved path that provisions or updates Azure resources and deploys application changes.

## Debugging

- Use workflow logs, Application Insights, component health endpoints, and approved read-only Azure CLI commands to diagnose deployed state.
- Compare the deployed configuration with the documented source of truth before changing infrastructure code.

## Non-standard procedures

Do not run `pulumi up`, `pulumi destroy`, direct Docker builds/pushes, ACR builds, or Azure write commands from a local session. Use the pipeline for all deployment actions.
