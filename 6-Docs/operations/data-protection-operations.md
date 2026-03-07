# Data Protection Key Persistence — Operations Guide

**Component:** `MotorcycleRag.WebUI.BFF`  
**Audience:** DevOps Engineers, SREs  
**Review Cadence:** Quarterly, or after any storage account change

---

## Table of Contents

1. [Overview](#overview)
2. [Health Check Monitoring](#health-check-monitoring)
3. [Application Insights Metrics](#application-insights-metrics)
4. [Application Insights Events](#application-insights-events)
5. [Log Monitoring](#log-monitoring)
6. [Alerts to Configure](#alerts-to-configure)
7. [Key Lifecycle Management](#key-lifecycle-management)
8. [Routine Operational Tasks](#routine-operational-tasks)
9. [Related Documentation](#related-documentation)

---

## Overview

The Data Protection system protects session cookies for the BFF service. Operational monitoring focuses on three concerns:

1. **Availability** — Can the service read and write keys to blob storage?
2. **Integrity** — Are keys being persisted correctly and not expiring unexpectedly?
3. **Performance** — Is key persistence adding unacceptable latency?

The primary monitoring surface is:
- The `/health` endpoint for availability
- Application Insights metrics and custom events for integrity and performance
- Azure Monitor alerts for automated notification

---

## Health Check Monitoring

### Endpoint

```
GET https://<bff-hostname>/health
```

The health response includes a `data_protection_blob` entry populated by [`DataProtectionHealthCheck`](../../1-Presentation/MotorcycleRag.WebUI.BFF/HealthChecks/DataProtectionHealthCheck.cs:12).

### Interpreting Results

| `data_protection_blob` Status | Meaning | Action |
|---|---|---|
| `Healthy` | Blob storage is accessible. Keys are persisted correctly. | None |
| `Degraded` | `DataProtection:BlobUri` is not configured. Ephemeral keys in use. Sessions will not survive restarts. | Configure `DataProtection:BlobUri` immediately in non-Development environments. |
| `Unhealthy` | Unexpected error connecting to blob storage. | See [Troubleshooting Guide](data-protection-troubleshooting.md) |

### Reading Health in Azure Container Apps

```bash
# Tail live container logs to watch health probe results
az containerapp logs show \
  --name <container-app-name> \
  --resource-group <resource-group> \
  --follow \
  --tail 50 \
  | grep -i "data_protection_blob\|HealthCheck"
```

### Automated Health Probe

Azure Container Apps polls `/health` as a liveness and readiness probe. Configure in your IaC to ensure restarts are triggered on repeated `Unhealthy` responses.

---

## Application Insights Metrics

The [`DataProtectionMonitoringService`](../../1-Presentation/MotorcycleRag.WebUI.BFF/Extensions/DataProtectionMonitoringExtensions.cs:11) emits these metrics to Application Insights.

### Metric: `DataProtection.KeyPersistence.Success`

**Type:** Counter  
**Description:** Incremented each time keys are successfully written to blob storage.  
**Expected behavior:** Should increment on every application startup and on key rotation events.

**KQL Query:**

```kusto
customMetrics
| where name == "DataProtection.KeyPersistence.Success"
| summarize total = sum(value) by bin(timestamp, 1h)
| render timechart
```

### Metric: `DataProtection.KeyPersistence.Failure`

**Type:** Counter  
**Description:** Incremented each time key persistence fails.  
**Expected behavior:** Must remain at 0. Any non-zero value requires immediate investigation.

**KQL Query:**

```kusto
customMetrics
| where name == "DataProtection.KeyPersistence.Failure"
| where value > 0
| project timestamp, value, customDimensions
| order by timestamp desc
```

### Metric: `DataProtection.KeyPersistence.Duration`

**Type:** Duration (milliseconds)  
**Description:** Time taken to write keys to blob storage.  
**Expected behavior:** Should be under 500ms in normal operation. Spikes may indicate storage account throttling.

**KQL Query:**

```kusto
customMetrics
| where name == "DataProtection.KeyPersistence.Duration"
| summarize avg(value), max(value), percentile(value, 95) by bin(timestamp, 1h)
| render timechart
```

---

## Application Insights Events

### Event: `DataProtection.KeysInitialized`

Emitted on application startup when Data Protection keys are loaded from or written to blob storage.

**KQL Query:**

```kusto
customEvents
| where name == "DataProtection.KeysInitialized"
| project timestamp, customDimensions.BlobUri, customDimensions.CorrelationId
| order by timestamp desc
| take 20
```

**Operational use:** Confirms each container revision started with persistent keys. The absence of this event on startup indicates the BlobUri was not configured.

### Event: `DataProtection.KeysRotated`

Emitted when a new key is generated as part of the rotation cycle.

**KQL Query:**

```kusto
customEvents
| where name == "DataProtection.KeysRotated"
| project timestamp, customDimensions.BlobUri
| order by timestamp desc
| take 10
```

**Operational use:** Track rotation frequency. ASP.NET Core rotates keys every 90 days by default, generating a new key 14 days before the current key expires.

### Event: `DataProtection.KeysLoaded`

Emitted when keys are read from blob storage (typically on startup).

**KQL Query:**

```kusto
customEvents
| where name == "DataProtection.KeysLoaded"
| project timestamp, customDimensions.BlobUri
| order by timestamp desc
| take 20
```

---

## Log Monitoring

### Structured Log Entries

| Log Level | Message Pattern | Meaning |
|---|---|---|
| `Information` | `Data Protection keys persisted to Azure Blob Storage: {BlobUri}` | Startup: keys configured correctly |
| `Warning` | `Data Protection is using ephemeral keys - sessions will NOT survive container restarts` | BlobUri not configured |
| `Debug` | `Data Protection blob storage is accessible: {BlobUri}` | Health check passed |
| `Debug` | `Data Protection blob does not exist yet, verifying write access: {BlobUri}` | First deployment — blob will be created |
| `Error` | `Failed to access Data Protection blob storage: {BlobUri}` | RBAC or network issue |
| `Error` | `Unexpected error checking Data Protection blob storage: {BlobUri}` | Unhandled exception in health check |

### Searching Logs in Application Insights

```kusto
traces
| where timestamp > ago(24h)
| where message contains "Data Protection"
| project timestamp, severityLevel, message, operation_Id
| order by timestamp desc
```

### Searching for IDX21329 Errors

```kusto
exceptions
| where timestamp > ago(1h)
| where outerMessage contains "IDX21329" or type contains "SecurityTokenDecryptionFailedException"
| project timestamp, type, outerMessage, cloud_RoleInstance, operation_Id
| order by timestamp desc
```

---

## Alerts to Configure

Configure these alerts in Azure Monitor on the Application Insights resource.

### Alert 1: Key Persistence Failure

| Property | Value |
|---|---|
| **Alert name** | `DP-KeyPersistenceFailure` |
| **Severity** | P1 — Critical |
| **Signal type** | Custom metric |
| **Metric** | `DataProtection.KeyPersistence.Failure` |
| **Condition** | Total > 0 in last 5 minutes |
| **Action** | Page on-call SRE |

**Rationale:** Any failure to persist keys risks session invalidation on the next container restart.

### Alert 2: Health Check Degraded

| Property | Value |
|---|---|
| **Alert name** | `DP-HealthDegraded` |
| **Severity** | P2 — High |
| **Signal type** | Log search |
| **Query** | `customEvents \| where name == "DataProtection.KeysInitialized" \| where customDimensions.BlobUri == "not-configured"` |
| **Condition** | Result count > 0 in last 15 minutes |
| **Action** | Notify on-call engineer |

### Alert 3: IDX21329 Errors Detected

| Property | Value |
|---|---|
| **Alert name** | `DP-IDX21329Errors` |
| **Severity** | P1 — Critical |
| **Signal type** | Log search |
| **Query** | `exceptions \| where outerMessage contains "IDX21329"` |
| **Condition** | Result count > 5 in last 10 minutes |
| **Action** | Page on-call SRE |

**Rationale:** Multiple IDX21329 errors indicate active session invalidation affecting users.

### Alert 4: Slow Key Persistence

| Property | Value |
|---|---|
| **Alert name** | `DP-SlowKeyPersistence` |
| **Severity** | P3 — Warning |
| **Signal type** | Custom metric |
| **Metric** | `DataProtection.KeyPersistence.Duration` |
| **Condition** | P95 > 2000ms in last 30 minutes |
| **Action** | Notify on-call engineer |

**Rationale:** Slow persistence may indicate storage account throttling that could escalate to failures.

---

## Key Lifecycle Management

ASP.NET Core Data Protection manages key lifecycle automatically. Understanding the defaults helps operators anticipate rotation events.

| Event | Default Timing |
|---|---|
| New key generation | 14 days before current key expires |
| Key active period | 90 days from creation |
| Key retained (for decryption) | 14 days after expiry |
| Key ring refresh from blob | Every 24 hours (or on next startup) |

### Viewing Active Keys

The `keys.xml` blob contains the key ring. To inspect it (read-only):

```bash
az storage blob download \
  --account-name <storage-account> \
  --container-name <container> \
  --name keys.xml \
  --file /tmp/keys.xml \
  --auth-mode login && cat /tmp/keys.xml
```

> **Security Note:** The `keys.xml` file contains plaintext encryption key material. Handle with care and do not log or share its contents. Delete `/tmp/keys.xml` after inspection.

---

## Routine Operational Tasks

### Verify Configuration After Deployment

After every deployment to a non-Development environment:

1. Check the health endpoint:
   ```bash
   curl https://<bff-hostname>/health | python -m json.tool
   ```
2. Confirm `data_protection_blob` status is `Healthy`.
3. Check Application Insights for `DataProtection.KeysInitialized` event within the last 5 minutes.

### Monthly Review

1. Query `DataProtection.KeyPersistence.Failure` metric for the past 30 days — value must be 0.
2. Query `DataProtection.KeysRotated` events — confirm rotation occurred if keys are older than 76 days.
3. Review blob storage access logs for unauthorized access attempts.

### Quarterly Review

1. Verify the Managed Identity still has `Storage Blob Data Contributor` on the storage container.
2. Confirm the storage account has no public access enabled.
3. Review key expiry dates in `keys.xml` and confirm no keys are within 7 days of expiry without a new key present.

---

## Related Documentation

- [Architecture Guide](data-protection-architecture.md)
- [Troubleshooting Guide](data-protection-troubleshooting.md)
- [Disaster Recovery Guide](data-protection-disaster-recovery.md)
