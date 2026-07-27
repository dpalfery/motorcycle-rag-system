---
id: operations/webui-bff-data-protection-disaster-recovery
title: Data Protection Key Persistence — Disaster Recovery Guide
doc-type: runbook
status: current
component: MotorcycleRAG Web UI BFF
source-root: 1-Presentation/MotorcycleRag.WebUI.BFF
owner: Web UI maintainers
last-reviewed: 2026-07-21
code-refs:
  - DataProtectionServiceConfiguration
  - AddBffDataProtection
  - AzureBlobDataProtectionProbe
api-endpoints: []
decided-by: []
supersedes: []
---

# Data Protection Key Persistence — Disaster Recovery Guide

**Audience:** DevOps Engineers, SREs  
**Approval Required:** Team Lead sign-off before executing any key rotation or deletion procedure

---

## Table of Contents

1. [Overview and Impact](#overview-and-impact)
2. [Backing Up Encryption Keys](#backing-up-encryption-keys)
3. [Restoring Encryption Keys](#restoring-encryption-keys)
4. [Recovering from Corrupted or Expired Keys](#recovering-from-corrupted-or-expired-keys)
5. [Manual Key Rotation](#manual-key-rotation)
6. [Storage Account Failure Recovery](#storage-account-failure-recovery)
7. [Decision Tree](#decision-tree)
8. [Related Documentation](#related-documentation)

---

## Overview and Impact

### What Is the Key Ring?

The `keys.xml` blob in Azure Blob Storage contains the ASP.NET Core Data Protection key ring. This file holds the symmetric encryption keys that protect all session cookies for the BFF service.

### Impact of Key Loss

| Scenario | User Impact | Recovery Time |
| --- | --- | --- |
| Temporary blob unavailability | No new logins; existing sessions may fail | Minutes (blob restored) |
| Key ring blob deleted | All active sessions invalidated; all users logged out | ~5 minutes (restore from backup) |
| Keys corrupted | All active sessions invalidated; all users logged out | ~5 minutes (restore from backup) |
| All keys expired | Sessions older than key expiry fail; new sessions work | Manual rotation required |
| No backup exists | Permanent loss of active sessions | Users must log in again |

### Key Principle

**Key loss is a user experience issue, not a data breach.** Session cookies encrypted with lost keys cannot be decrypted, but no user data is exposed. Users are logged out and must authenticate again.

---

## Backing Up Encryption Keys

### Automated Backup (Recommended)

Configure Azure Storage lifecycle policies and soft-delete on the storage account to retain deleted blobs for 30 days. This is your primary backup mechanism and should be configured via IaC.

To verify soft-delete is enabled (read-only check):

```bash
az storage blob service-properties show \
  --account-name <storage-account> \
  --auth-mode login \
  --query "deleteRetentionPolicy"
```

Expected output:

```json
{
  "days": 30,
  "enabled": true
}
```

### Manual Backup Before Planned Changes

Before any planned maintenance that could affect the storage account or key ring, create a manual backup:

```bash
# Download current keys.xml
az storage blob download \
  --account-name <storage-account> \
  --container-name <container> \
  --name keys.xml \
  --file ./keys-backup-$(date +%Y%m%d-%H%M%S).xml \
  --auth-mode login

# Verify the backup was created
ls -la keys-backup-*.xml
```

> **Security:** The backup file contains plaintext encryption key material. Store it securely (encrypted, access-controlled) and delete it when no longer needed. Do not commit it to version control or store it in shared drives.

### Backup Storage Location

Store manual backups in:

- An Azure Key Vault secret (base64-encoded XML content)
- A separate storage account with restricted access

Do not store backups in:

- Git repositories
- Shared file systems
- Email attachments
- Unencrypted local disks

---

## Restoring Encryption Keys

### Scenario: Blob Accidentally Deleted (Soft-Delete Enabled)

If soft-delete is enabled on the storage account, deleted blobs are retained for 30 days and can be restored.

```bash
# List deleted blobs in the container
az storage blob list \
  --account-name <storage-account> \
  --container-name <container> \
  --include d \
  --auth-mode login \
  --query "[?deleted].{name:name, deletedTime:properties.deletedTime}"

# Undelete the blob
az storage blob undelete \
  --account-name <storage-account> \
  --container-name <container> \
  --name keys.xml \
  --auth-mode login
```

After restoring, restart the container app to force it to reload the key ring:

```bash
az containerapp revision list \
  --name <container-app-name> \
  --resource-group <resource-group> \
  --query "[].name" \
  --output tsv
```

> **Note:** Container restarts must go through the deployment pipeline. Raise this with the team lead to trigger a new revision via the pipeline.

### Scenario: Restore from Manual Backup

If you have a manual backup of `keys.xml`:

```bash
# Upload backup to restore the key ring
az storage blob upload \
  --account-name <storage-account> \
  --container-name <container> \
  --name keys.xml \
  --file ./keys-backup-<timestamp>.xml \
  --overwrite true \
  --auth-mode login
```

> **Requires approval** — uploading to the storage account is an Azure write operation. Obtain team lead sign-off before executing.

After uploading, verify the blob:

```bash
az storage blob show \
  --account-name <storage-account> \
  --container-name <container> \
  --name keys.xml \
  --auth-mode login \
  --query "{lastModified: properties.lastModified, size: properties.contentLength}"
```

Then trigger a new container revision via the deployment pipeline to reload keys.

### Scenario: No Backup Available

If no backup exists and the key ring is lost:

1. Accept that all active sessions are invalidated — users must log in again.
2. Ensure `DataProtection:BlobUri` is configured correctly (see [Troubleshooting Guide](webui-bff-data-protection-troubleshooting.md)).
3. Deploy a new revision. On startup, ASP.NET Core will generate a fresh key ring and write it to `keys.xml`.
4. Verify the health check returns `Healthy`.
5. Notify users via your standard incident communication channel.

---

## Recovering from Corrupted or Expired Keys

### Detecting Corruption

Signs that `keys.xml` may be corrupted:

- `IDX21329` errors in Application Insights for all users, including newly logged-in users
- Application logs contain: `System.Xml.XmlException` or `CryptographicException` when loading keys
- Health check returns `Unhealthy` even after RBAC and network issues are ruled out

### Recovery Procedure

1. **Download the current (potentially corrupted) file for forensic analysis:**

   ```bash
   az storage blob download \
     --account-name <storage-account> \
     --container-name <container> \
     --name keys.xml \
     --file ./keys-corrupted-$(date +%Y%m%d-%H%M%S).xml \
     --auth-mode login
   ```

2. **Attempt to restore from backup** (see [Restoring Encryption Keys](#restoring-encryption-keys)).

3. **If no backup exists**, delete the corrupted file:

   ```bash
   az storage blob delete \
     --account-name <storage-account> \
     --container-name <container> \
     --name keys.xml \
     --auth-mode login
   ```

   > **Requires approval.** This action invalidates all active sessions.

4. **Deploy a new container revision** via the pipeline. ASP.NET Core will generate a new key ring on startup.

5. **Verify recovery:**

   ```bash
   curl https://<bff-hostname>/health | python -m json.tool
   ```

   The `data_protection_blob` status must return `Healthy`.

---

## Manual Key Rotation

ASP.NET Core Data Protection rotates keys automatically (every 90 days by default). You should only trigger manual rotation in the following cases:

- You suspect key compromise (treat as a security incident).
- Compliance requirements mandate more frequent rotation.
- A key is about to expire and automated rotation has not triggered.

### Procedure: Force Key Rotation via Deployment

The safest way to force key rotation is to deploy a new revision with a fresh key ring:

1. **Backup current keys:**

   ```bash
   az storage blob download \
     --account-name <storage-account> \
     --container-name <container> \
     --name keys.xml \
     --file ./keys-pre-rotation-$(date +%Y%m%d-%H%M%S).xml \
     --auth-mode login
   ```

2. **Delete the current key ring blob:**

   ```bash
   az storage blob delete \
     --account-name <storage-account> \
     --container-name <container> \
     --name keys.xml \
     --auth-mode login
   ```

   > **Requires approval.** This invalidates all active sessions.

3. **Deploy a new container revision** via the pipeline to generate a fresh key ring.

4. **Verify:**

   ```bash
   curl https://<bff-hostname>/health | python -m json.tool
   ```

### Procedure: Revoke a Specific Key (Security Incident)

If you need to revoke a specific key (for example, after a suspected compromise):

1. Download `keys.xml`.
2. Open the file and locate the `<key>` element with the compromised key ID.
3. Add a `<revocation>` element for that key ID following the [ASP.NET Core Data Protection XML format](https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-storage-format).
4. Upload the modified `keys.xml` back to blob storage.
5. Deploy a new revision to force a reload.

> **This procedure requires expertise in ASP.NET Core Data Protection internals. Engage a senior engineer.**

---

## Storage Account Failure Recovery

If the Azure Storage account hosting `keys.xml` becomes unavailable:

### Immediate Impact

- The BFF service continues to operate using the in-memory copy of the key ring loaded at startup.
- New key persistence operations fail silently (tracked in Application Insights under `DataProtection.KeyPersistence.Failure`).
- Sessions remain valid until the container restarts or the in-memory key ring expires.

### Recovery Steps

1. **Monitor storage account availability:**

   ```bash
   az storage account show \
     --name <storage-account> \
     --resource-group <resource-group> \
     --query "statusOfPrimary"
   ```

2. **Check Azure Service Health** in the Azure Portal for regional storage outages.

3. **Do not restart containers** during a storage outage — the in-memory key ring will be lost and recovery will require the storage to be available.

4. Once storage is restored, verify the health check:

   ```bash
   curl https://<bff-hostname>/health | python -m json.tool
   ```

5. If the `keys.xml` blob was lost during the outage, follow the [Restoring Encryption Keys](#restoring-encryption-keys) procedure.

### Geo-Redundancy

To improve resilience against regional storage failures, configure the storage account with **Geo-Redundant Storage (GRS)** or **Zone-Redundant Storage (ZRS)** via IaC. This does not require code changes.

---

## Decision Tree

Use this decision tree when responding to a Data Protection incident:

```text
Are users being logged out unexpectedly?
│
├─ YES → Check /health endpoint
│        │
│        ├─ data_protection_blob = Degraded?
│        │   → BlobUri not configured. See: Troubleshooting Guide → Fix 1
│        │
│        ├─ data_protection_blob = Unhealthy?
│        │   → RBAC or network issue. See: Troubleshooting Guide → Fix 2/4
│        │
│        └─ data_protection_blob = Healthy but errors continue?
│            → Check Application Insights for IDX21329
│            │
│            ├─ IDX21329 in logs?
│            │   → Check if keys.xml exists and is not corrupted
│            │   → See: Recovering from Corrupted or Expired Keys
│            │
│            └─ No IDX21329?
│                → Escalate to senior engineer
│
└─ NO → Routine monitoring only. No action required.
```

---

## Related Documentation

- [Architecture Guide](../MotorcycleRag.WebUI.BFF/data-protection.md)
- [Troubleshooting Guide](webui-bff-data-protection-troubleshooting.md)
- [Operations Guide](webui-bff-data-protection-operations.md)
- [ASP.NET Core Data Protection Key Management (Microsoft Learn)](https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-management)
- [Azure Blob Storage Soft Delete (Microsoft Learn)](https://learn.microsoft.com/azure/storage/blobs/soft-delete-blob-overview)
