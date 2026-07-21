---
id: operations/webui-bff-data-protection-troubleshooting
title: Data Protection — Troubleshooting Runbook
doc-type: runbook
status: current
component: MotorcycleRAG Web UI BFF
source-root: 1-Presentation/MotorcycleRag.WebUI.BFF
owner: Web UI maintainers
last-reviewed: 2026-07-21
code-refs:
  - DataProtectionServiceConfiguration
  - AddBffDataProtection
  - DataProtectionHealthCheck
  - IDataProtectionBlobProbe
api-endpoints: []
decided-by: []
supersedes: []
---

# Data Protection — Troubleshooting Runbook

**Audience:** DevOps Engineers, SREs  
**Severity:** P0 — Session invalidation affects all authenticated users

---

## Table of Contents

1. [IDX21329 Error — What It Is](#idx21329-error--what-it-is)
2. [Common Causes](#common-causes)
3. [Symptoms](#symptoms)
4. [Diagnosis Procedure](#diagnosis-procedure)
5. [Fixes](#fixes)
6. [Verification](#verification)
7. [Escalation Path](#escalation-path)

---

## IDX21329 Error — What It Is

**Error message:**

```text
IDX21329: Unable to validate token. Decryption failed. Keys tried: <key-id>.
```

This error occurs when the ASP.NET Core Data Protection system cannot find the encryption key that was used to protect a session cookie. The key is identified by the key ID embedded in the cookie.

**In the BFF context**, this means:

1. A user logged in when the application was using Key A.
2. The application restarted or scaled out.
3. The new instance does not have Key A (ephemeral keys) or Key A expired.
4. The user's cookie cannot be decrypted.
5. The user is silently redirected to the login page.

This is **not a JWT token validation error** in the traditional sense — it is a cookie decryption failure that surfaces as an `IDX21329` exception in the Microsoft Identity stack.

---

## Common Causes

### Cause 1: Ephemeral Keys (Missing BlobUri Configuration)

**What happens:** `DataProtection:BlobUri` is not configured. Each container instance generates its own in-memory key ring. When the container restarts or a new replica starts, the key ring is lost.

**Indicators:**

- Health check returns `Degraded` with message: `DataProtection:BlobUri is not configured - using ephemeral keys`
- Log entry: `Data Protection is using ephemeral keys - sessions will NOT survive container restarts`

### Cause 2: Blob Storage Access Denied

**What happens:** `DataProtection:BlobUri` is configured, but the container's Managed Identity lacks the **Storage Blob Data Contributor** role. Keys cannot be persisted or read from blob storage. The application falls back to ephemeral keys.

**Indicators:**

- Health check returns `Degraded` or `Unhealthy` with an Azure `RequestFailedException`
- Log entry: `Failed to access Data Protection blob storage`
- Azure Storage error code: `AuthorizationPermissionMismatch` or `403 Forbidden`

### Cause 3: Incorrect Blob URI

**What happens:** `DataProtection:BlobUri` is set to a malformed or incorrect URI. The blob cannot be located.

**Indicators:**

- Startup exception (if the URI is clearly invalid)
- Health check returns `Unhealthy`
- Azure Storage error code: `BlobNotFound`, `ContainerNotFound`, or `InvalidUri`

### Cause 4: Storage Account Network Restrictions

**What happens:** The Azure Storage account has firewall rules or private endpoint policies that block access from the container app's subnet.

**Indicators:**

- Health check returns `Unhealthy` with a network timeout or `403 Forbidden`
- Azure Storage error code: `AuthorizationFailure` or network timeout

### Cause 5: Key Expiry Without Rotation

**What happens:** All active keys in the key ring have passed their expiry date (default: 90 days) and no new keys were generated (possibly due to blob access issues during rotation).

**Indicators:**

- Error occurs gradually for users with older sessions
- Newer sessions work; older sessions fail

---

## Symptoms

| Symptom | Likely Cause |
| --- | --- |
| All users logged out after deployment | Ephemeral keys — new container without BlobUri |
| Users logged out intermittently during scale events | Ephemeral keys — multiple replicas with different key rings |
| `/health` returns `Degraded` | BlobUri not configured |
| `/health` returns `Unhealthy` | RBAC, network, or URI misconfiguration |
| `IDX21329` in Application Insights exceptions | Any of the above causes |
| Startup fails with `InvalidOperationException` in Production | `DataProtection:BlobUri` missing in production environment |

---

## Diagnosis Procedure

### Step 1: Check the Health Endpoint

```bash
curl https://<bff-hostname>/health
```

**Expected healthy response:**

```json
{
  "status": "Healthy",
  "results": {
    "data_protection_blob": {
      "status": "Healthy",
      "description": "Data Protection blob storage is accessible: <account>.blob.core.windows.net/<container>"
    }
  }
}
```

**Degraded response (ephemeral keys):**

```json
{
  "status": "Degraded",
  "results": {
    "data_protection_blob": {
      "status": "Degraded",
      "description": "DataProtection:BlobUri is not configured - using ephemeral keys"
    }
  }
}
```

### Step 2: Check Container App Logs

```bash
az containerapp logs show \
  --name <container-app-name> \
  --resource-group <resource-group> \
  --follow \
  --tail 100
```

Filter for Data Protection log lines:

```bash
az containerapp logs show \
  --name <container-app-name> \
  --resource-group <resource-group> \
  --tail 200 \
  | grep -i "DataProtection\|data protection\|IDX21329"
```

**Look for:**

| Log Entry | Meaning |
| --- | --- |
| `Data Protection keys persisted to Azure Blob Storage: ...` | Keys configured correctly |
| `Data Protection is using ephemeral keys` | BlobUri not configured |
| `Failed to access Data Protection blob storage` | RBAC or network issue |

### Step 3: Verify the BlobUri Configuration

```bash
az appconfig kv show \
  --name <app-config-name> \
  --key "DataProtection:BlobUri" \
  --label "bff"
```

Verify the URI format:

```text
https://<storage-account>.blob.core.windows.net/<container>/keys.xml
```

### Step 4: Verify RBAC Assignment

```bash
# Get the Managed Identity Object ID for the container app
az containerapp show \
  --name <container-app-name> \
  --resource-group <resource-group> \
  --query "identity.principalId" \
  --output tsv

# Check role assignments on the storage container
az role assignment list \
  --assignee <managed-identity-object-id> \
  --scope /subscriptions/<subscription-id>/resourceGroups/<rg>/providers/Microsoft.Storage/storageAccounts/<account>/blobServices/default/containers/<container>
```

The Managed Identity must have `Storage Blob Data Contributor` (role ID: `ba92f5b4-2d11-453d-a403-e96b0029c9fe`).

### Step 5: Verify the Blob Exists

```bash
az storage blob show \
  --account-name <storage-account> \
  --container-name <container> \
  --name keys.xml \
  --auth-mode login
```

If the blob does not exist yet (first deployment), that is expected — the Data Protection system will create it on the next startup.

### Step 6: Check Application Insights

In Application Insights, run this query to find the specific error:

```kusto
exceptions
| where timestamp > ago(1h)
| where type contains "IDX21329" or outerMessage contains "IDX21329"
| project timestamp, type, outerMessage, severityLevel
| order by timestamp desc
```

Also search for Data Protection custom events:

```kusto
customEvents
| where timestamp > ago(24h)
| where name startswith "DataProtection."
| project timestamp, name, customDimensions
| order by timestamp desc
```

---

## Fixes

### Fix 1: Configure DataProtection:BlobUri

Set the configuration value in Azure App Configuration:

> **Note:** The `az appconfig` commands below are read-only diagnostic examples. To create or update configuration values, commit the change through the infrastructure-as-code pipeline.

Verify the value exists:

```bash
az appconfig kv show \
  --name <app-config-name> \
  --key "DataProtection:BlobUri" \
  --label "bff"
```

The correct URI format is:

```text
https://<storage-account-name>.blob.core.windows.net/<container-name>/keys.xml
```

**To apply the change:** Update the value in your IaC configuration and push to the `develop` or `main` branch to trigger the deployment pipeline. Do not use `az appconfig kv set` directly.

After the pipeline deploys, restart the container app revision to pick up the new configuration.

### Fix 2: Grant RBAC to Managed Identity

> **Requires approval** — this is an Azure resource modification.

Raise this with the team lead to grant the role via IaC:

```text
Role:  Storage Blob Data Contributor
Scope: /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.Storage/storageAccounts/<account>/blobServices/default/containers/<container>
Principal: <managed-identity-object-id>
```

### Fix 3: Correct a Malformed BlobUri

1. Identify the current (incorrect) URI using Step 3 above.
2. Construct the correct URI:

```text
   https://<account>.blob.core.windows.net/<container>/keys.xml
   ```

1. Update via IaC and deploy.

### Fix 4: Unblock Storage Account Network Access

> **Requires approval** — this is an Azure resource modification.

If the storage account has firewall rules:

```bash
# Read-only: check current network rules
az storage account show \
  --name <storage-account> \
  --resource-group <resource-group> \
  --query "networkRuleSet"
```

Raise a change request to add the container app's outbound IP range or virtual network to the storage account's allowed network list via IaC.

### Fix 5: Recover from Expired Keys

If all keys have expired, you must regenerate the key ring. See the [Disaster Recovery Guide](webui-bff-data-protection-disaster-recovery.md#recovering-from-corrupted-or-expired-keys).

---

## Verification

After applying a fix:

### 1. Confirm Health is Healthy

```bash
curl https://<bff-hostname>/health | python -m json.tool
```

The `data_protection_blob` check must return `Healthy`.

### 2. Confirm Keys Are Written to Blob

```bash
az storage blob show \
  --account-name <storage-account> \
  --container-name <container> \
  --name keys.xml \
  --auth-mode login \
  --query "{lastModified: properties.lastModified, size: properties.contentLength}"
```

### 3. Confirm Startup Log

```bash
az containerapp logs show \
  --name <container-app-name> \
  --resource-group <resource-group> \
  --tail 50 \
  | grep "Data Protection"
```

Expected:

```text
Data Protection keys persisted to Azure Blob Storage: https://...
```

### 4. Verify IDX21329 Errors Stop

After the fix, confirm no new `IDX21329` exceptions appear in Application Insights within 15 minutes:

```kusto
exceptions
| where timestamp > ago(15m)
| where outerMessage contains "IDX21329"
| count
```

---

## Escalation Path

| Situation | Action |
| --- | --- |
| BlobUri is configured but health check still fails | Escalate to team lead; likely a network or RBAC issue requiring Azure portal access |
| IDX21329 continues after fix applied | Check if old container revisions are still running; force a new revision |
| Keys.xml blob is corrupt | Follow [Disaster Recovery Guide](webui-bff-data-protection-disaster-recovery.md) |
| Storage account is unavailable | Engage Azure Support; consider DR procedure |

---

## Related Documentation

- [Architecture Guide](../MotorcycleRag.WebUI.BFF/data-protection.md)
- [Operations Guide](webui-bff-data-protection-operations.md)
- [Disaster Recovery Guide](webui-bff-data-protection-disaster-recovery.md)
