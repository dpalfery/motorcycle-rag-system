---
id: azure/agent-access
title: Azure Environment
doc-type: governance
status: current
component: Azure Environment
owner: Platform maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# Azure Environment

## Development Environment

- Windows 11, WSL2, Docker
- Assume Windows-native solutions unless easy Docker alternative exists

## Azure Subscription Allowlist

Before running any `az` command, **always** verify the active subscription is in the allowlist. If it is not, stop and ask the user — never run `az` commands against a client or unknown subscription.

| Subscription ID | Name | Purpose |
| --- | --- | --- |
| `5df33f46-892f-4dc1-9d0c-701464efd7e5` | MotorcycleRAG (personal) | Primary — this project's Azure resources live here |
| *(add more as needed)* | | |

**Guard rail**: run `az account show --query id -o tsv` before any `az` read operation. If the output is not in the list above, do **not** proceed — flag it to the user.

## Azure Tenant Map

- **EPAM tenant**: compute and hosting tenant for this solution. This is where the Azure hosting resources for the repo live, including the subscription backed by MSDN credits.
- **`palfery.onmicrosoft.com` tenant**: David's primary Entra ID tenant and the home for personal Azure subscriptions.
- **`0f8f8a52-f135-43af-af88-e0b54ca9ff91` tenant**: the Entra External ID tenant associated with `palfery.onmicrosoft.com`. **All app registrations for this solution (API, Admin, BFF, Mobile) live in this tenant.**
- **Do not assume** Azure hosting resources, personal subscriptions, workforce app registrations, and External ID objects live in the same tenant.
- Before using `az` or checking Entra objects, verify which tenant actually owns the target resource or identity.
