# Azure Naming Standards

> **Source:** [Microsoft Cloud Adoption Framework – Landing Zone Naming convention](https://learn.microsoft.com/azure/cloud-adoption-framework/ready/landing-zone/design-area/naming)
>
> This document defines the canonical naming convention for **Motorcycle RAG System** Azure resources. It is aligned with the Microsoft **Cloud Adoption Framework (CAF)** guidance and leverages the [CAF Naming module slugs](https://learn.microsoft.com/azure/cloud-adoption-framework/ready/landing-zone/design-area/naming#caf-naming-tool) wherever applicable.

---
## 1. General Pattern

```
<org>-<workload>-<env>-<loc>-<resType>[<instance>]
```

| Placeholder | Description | Example |
| --- | --- | --- |
| `<org>` | Organisation or business unit acronym (2–4 chars). | `mcr` |
| `<workload>` | Application-level name (3–8 chars). | `rag` |
| `<env>` | Environment code (see §2). | `dev` |
| `<loc>` | Azure region short code (see §3). | `eus2` |
| `<resType>` | Resource Type slug from the CAF Naming module (see §4). | `vm`, `kv` |
| `[<instance>]` | Optional numeric or alpha suffix for multiple instances. | `01`, `b` |

> **Max length:** Some services (e.g., Storage accounts) impose strict limits (≤ 24 chars) and character rules. When necessary, abbreviate `<workload>` or drop dashes to keep within limits.

---
## 2. Environment Codes

| Environment | Code |
| --- | --- |
| Development | `dev` |
| Test / QA | `tst` |
| Staging | `stg` |
| Production | `prd` |
| Sandbox / Personal | `sbx` |

---
## 3. Azure Region Short Codes (subset)

| Region | Code |
| --- | --- |
| East US | `eus` |
| East US 2 | `eus2` |
| West Europe | `weu` |
| North Europe | `neu` |
| UK South | `uks` |
| Australia East | `aue` |

_For the full list, refer to the CAF article._

---
## 4. Resource Type Slugs

The CAF Naming module assigns 2–4 character **slugs** to every Azure resource type. Use these values for `<resType>`.

| Resource | CAF Slug |
| --- | --- |
| Resource Group | `rg` |
| Virtual Network | `vnet` |
| Subnet | `snet` |
| Network Security Group | `nsg` |
| Route Table | `rt` |
| Public IP | `pip` |
| Network Interface | `nic` |
| Network Watcher | `nw` |
| Log Analytics Workspace | `log` |
| Key Vault | `kv` |
| Storage Account | `st` |
| App Service Plan | `asp` |
| Web App / Function App | `app` |
| Container Registry | `acr` |
| Container App | `capp` |
| Container App Environment | `cae` |
| Application Insights | `appi` |
| Azure Kubernetes Service | `aks` |
| SQL Server | `sql` |
| Cosmos DB Account | `cos` |
| Virtual Machine | `vm` |

> If a resource type is not listed, consult the [CAF naming tool](https://learn.microsoft.com/azure/cloud-adoption-framework/ready/landing-zone/design-area/naming#caf-naming-tool) and use the recommended slug.

---
## 5. Examples

| Resource | Example Name | Notes |
| --- | --- | --- |
| Resource Group | `mcr-rag-dev-eus2-rg` | Contains all dev resources in East US 2. |
| Storage Account | `mcrragdevst01` | 24-char limit, no dashes; `<org><workload><env><resType><instance>`. |
| Key Vault | `mcr-rag-prd-weu-kv` | Production vault in West Europe. |
| Container App | `mcr-rag-dev-eus2-capp` | Deployed by Pulumi stack. |
| Public IP | `mcr-rag-prd-eus-pip01` | First public IP for production. |

---
## 6. Decision Log

* **2025-07-26:** Initial version created in alignment with CAF guidance.