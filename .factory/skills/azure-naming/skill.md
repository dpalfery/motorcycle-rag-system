---
name: azure-naming
description: Use the Azure naming convention to name resources. 
---

# Azure Naming Standards

## you must follow the strict naming convention for All Azure resources must follow the strict naming convention:

`{slug}-{environment}-{region}-{appName}`

## Definitions

- **Slug**: The standard Azure resource abbreviation (see table below).
- **Environment**: The target environment code (e.g., `dev`, `qa`, `prod`, `stg`).
- **Region**: The Azure region short code (e.g., `eastus`, `centralus`, `westeurope`).
- **AppName**: The application or project name (e.g., `hotshot`).

## Examples

| Resource Type | Slug | Pattern | Example |
|Data | --- | --- | --- |
| Resource Group | `rg` | `rg-{env}-{region}-{app}` | `rg-dev-eastus-hotshot` |
| Key Vault | `kv` | `kv-{env}-{region}-{app}` | `kv-dev-eastus-hotshot` |
| App Service Plan | `asp` | `asp-{env}-{region}-{app}` | `asp-dev-eastus-hotshot` |
| Container App | `ca` | `ca-{env}-{region}-{app}` | `ca-dev-eastus-hotshot` |
| SQL Server | `sql` | `sql-{env}-{region}-{app}` | `sql-dev-eastus-hotshot` |
| SQL Database | `sqldb` | `sqldb-{env}-{region}-{app}` | `sqldb-dev-eastus-hotshot` |
| Log Analytics | `log` | `log-{env}-{region}-{app}` | `log-dev-eastus-hotshot` |
| App Insights | `appi` | `appi-{env}-{region}-{app}` | `appi-dev-eastus-hotshot` |
| Identity | `id` | `id-{env}-{region}-{app}` | `id-dev-eastus-hotshot` |
| App Configuration | `appcs` | `appcs-{env}-{region}-{app}` | `appcs-dev-eastus-hotshot` |
| Static Web App | `swa` | `swa-{env}-{region}-{app}` | `swa-dev-eastus-hotshot` |

## Exceptions and Restricted Naming

Some Azure resources have strict naming constraints (length limits, character restrictions). You MUST handle these as follows:

### 1. Key Vault
*   **Constraint**: Max 24 characters. Alphanumeric and hyphens.
*   **Standard**: `{slug}-{env}-{region}-{app}`
*   **Truncation Rule**: If the generated name exceeds 24 chars, you MUST abbreviate the `appName` or `region` to fit.
    *   *Example*: `kv-dev-eastus-hotshotlog` (truncated 'logistics' to 'log')

### 2. Storage Account
*   **Constraint**: Max 24 characters. **Lowercase alphanumeric ONLY**. No hyphens.
*   **Standard**: `{slug}{env}{region}{app}`
*   **Truncation Rule**: If exceeding 24 chars, abbreviate.
    *   *Example*: `stdeveastushotshot`

### 3. Container Registry
*   **Constraint**: Max 50 characters. Alphanumeric ONLY. No hyphens.
*   **Standard**: `{slug}{env}{region}{app}`
*   **Example**: `crdeveastushotshot`

### 4. SQL Server
*   **Constraint**: Max 63 characters. Lowercase alphanumeric and hyphens.
*   **Standard**: `{slug}-{env}-{region}-{app}`
*   **Example**: `sql-dev-eastus-hotshot`

## Summary Table

| Resource Type | Slug | Separator | Constraints | Pattern | Example |
|Data | --- | --- | --- | --- | --- |
| Resource Group | `rg` | `-` | 90 chars | `rg-{env}-{region}-{app}` | `rg-dev-eastus-hotshot` |
| **Key Vault** | `kv` | `-` | **24 chars**, no consecutive hyphens | `kv-{env}-{region}-{app}` | `kv-dev-eastus-hotshot` |
| **Storage Account**| `st` | *None* | **24 chars**, lowercase alphanumeric | `st{env}{region}{app}` | `stdeveastushotshot` |
| **Container Registry**| `cr` | *None* | 50 chars, alphanumeric | `cr{env}{region}{app}` | `crdeveastushotshot` |
| App Service Plan | `asp` | `-` | 40 chars | `asp-{env}-{region}-{app}` | `asp-dev-eastus-hotshot` |
| Container App | `ca` | `-` | 32 chars | `ca-{env}-{region}-{app}` | `ca-dev-eastus-hotshot` |
| SQL Server | `sql` | `-` | 63 chars | `sql-{env}-{region}-{app}` | `sql-dev-eastus-hotshot` |

## Enforcement

- **Pulumi/Terraform**: Construct names dynamically using variables.
  ```csharp
  // Correct
  var name = $"rg-{environment}-{location}-{appName}";
  
  // Incorrect
  var name = $"rg-hotshot-{environment}";
  ```
- **Validation**: 
  - Ensure variable order is strictly `{slug}-{env}-{region}-{app}`.
  - **Check length limits** in your code before creation. Throw an error if the generated name is too long.
