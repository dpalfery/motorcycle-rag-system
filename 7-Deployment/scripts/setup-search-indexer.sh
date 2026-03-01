#!/usr/bin/env bash
set -euo pipefail

###############################################################################
# setup-search-indexer.sh
#
# Idempotent script to configure an Azure AI Search indexer that ingests
# JSON Lines chunks from Azure Blob Storage.
#
# Prerequisites:
#   - The target index "motorcycle-index" must already exist.
#   - The storage account must grant the search service's managed identity
#     "Storage Blob Data Reader" access.
#
# Required environment variables:
#   AZURE_SEARCH_SERVICE      — Search service name (not full URL)
#   AZURE_SEARCH_API_KEY      — Admin API key
#   AZURE_STORAGE_ACCOUNT     — Storage account name
#   AZURE_SUBSCRIPTION_ID     — Azure subscription ID
#   AZURE_RESOURCE_GROUP      — Resource group containing the storage account
#
# Optional:
#   AZURE_STORAGE_CONTAINER   — Blob container name (default: "search-chunks")
###############################################################################

# ---------- validate required env vars ----------
required_vars=(
  AZURE_SEARCH_SERVICE
  AZURE_SEARCH_API_KEY
  AZURE_STORAGE_ACCOUNT
  AZURE_SUBSCRIPTION_ID
  AZURE_RESOURCE_GROUP
)

missing=()
for var in "${required_vars[@]}"; do
  if [[ -z "${!var:-}" ]]; then
    missing+=("$var")
  fi
done

if [[ ${#missing[@]} -gt 0 ]]; then
  echo "ERROR: Missing required environment variables:" >&2
  printf '  - %s\n' "${missing[@]}" >&2
  exit 1
fi

# ---------- derived vars ----------
SEARCH_ENDPOINT="https://${AZURE_SEARCH_SERVICE}.search.windows.net"
CONTAINER="${AZURE_STORAGE_CONTAINER:-search-chunks}"
INDEX_NAME="motorcycle-index"
DATASOURCE_NAME="motorcycle-blob-datasource"
INDEXER_NAME="motorcycle-blob-indexer"
API_VERSION="2024-05-01-preview"

echo "=== Azure AI Search Indexer Setup ==="
echo "  Search endpoint : ${SEARCH_ENDPOINT}"
echo "  Storage account : ${AZURE_STORAGE_ACCOUNT}"
echo "  Container       : ${CONTAINER}"
echo "  Index           : ${INDEX_NAME}"
echo "  Data source     : ${DATASOURCE_NAME}"
echo "  Indexer         : ${INDEXER_NAME}"
echo ""

# ---------- helper ----------
# PUT a JSON payload to the Search REST API.
# Usage: put_resource <path> <json_body>
put_resource() {
  local path="$1"
  local body="$2"

  local url="${SEARCH_ENDPOINT}${path}?api-version=${API_VERSION}"
  local http_code

  http_code=$(curl -s -o /tmp/search-api-response.json -w "%{http_code}" \
    -X PUT \
    -H "Content-Type: application/json" \
    -H "api-key: ${AZURE_SEARCH_API_KEY}" \
    -d "${body}" \
    "${url}")

  if [[ "${http_code}" -ge 200 && "${http_code}" -lt 300 ]]; then
    echo "  ✓ PUT ${path} — HTTP ${http_code}"
  elif [[ "${http_code}" == "409" ]]; then
    # 409 Conflict — resource already exists in identical state; safe to ignore
    echo "  ⚠ PUT ${path} — HTTP 409 (already exists, skipping)"
  else
    echo "  ✗ PUT ${path} — HTTP ${http_code}" >&2
    cat /tmp/search-api-response.json >&2
    echo "" >&2
    return 1
  fi
}

# ---------- 1. Create / update data source ----------
echo "→ Creating data source '${DATASOURCE_NAME}'..."

DATASOURCE_BODY=$(cat <<EOF
{
  "name": "${DATASOURCE_NAME}",
  "type": "azureblob",
  "credentials": {
    "connectionString": "ResourceId=/subscriptions/${AZURE_SUBSCRIPTION_ID}/resourceGroups/${AZURE_RESOURCE_GROUP}/providers/Microsoft.Storage/storageAccounts/${AZURE_STORAGE_ACCOUNT};"
  },
  "container": {
    "name": "${CONTAINER}"
  }
}
EOF
)

put_resource "/datasources/${DATASOURCE_NAME}" "${DATASOURCE_BODY}"

# ---------- 2. Create / update indexer ----------
echo "→ Creating indexer '${INDEXER_NAME}'..."

INDEXER_BODY=$(cat <<EOF
{
  "name": "${INDEXER_NAME}",
  "dataSourceName": "${DATASOURCE_NAME}",
  "targetIndexName": "${INDEX_NAME}",
  "schedule": {
    "interval": "PT5M"
  },
  "parameters": {
    "configuration": {
      "parsingMode": "jsonLines",
      "firstLineContainsHeaders": false
    }
  }
}
EOF
)

put_resource "/indexers/${INDEXER_NAME}" "${INDEXER_BODY}"

# ---------- done ----------
echo ""
echo "=== Setup complete ==="
echo "Indexer '${INDEXER_NAME}' is configured to poll '${CONTAINER}' every 5 minutes."
echo "Monitor status: ${SEARCH_ENDPOINT}/indexers/${INDEXER_NAME}/status?api-version=${API_VERSION}"
