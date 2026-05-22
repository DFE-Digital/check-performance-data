#!/usr/bin/env bash
#
# seed-rules-config.sh — upload the rules engine's seed JSON to a real Azure
# storage account. Idempotent: re-running just overwrites the blobs, which the
# worker's BlobRulesProvider picks up via its ETag-driven refresh.
#
# Usage:
#   ./scripts/seed-rules-config.sh <storage-account-name> <resource-group>
#
# Requires:
#   - az CLI logged in (az login) with rights on the target account
#   - bash, no other deps
#
# The script uses the canonical container name "rules-config" matching the
# default RulesEngineOptions__RulesBlobContainer.

set -euo pipefail

if [ "$#" -lt 2 ]; then
  echo "Usage: $0 <storage-account-name> <resource-group>" >&2
  exit 64
fi

STORAGE_ACCOUNT="$1"
RESOURCE_GROUP="$2"
CONTAINER="${RULES_CONTAINER:-rules-config}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SEED_DIR="${SCRIPT_DIR}/../src/DfE.CheckPerformanceData.RulesEngineWorker/seed"

if [ ! -d "$SEED_DIR" ]; then
  echo "Seed directory not found: $SEED_DIR" >&2
  exit 1
fi

echo "Ensuring container '$CONTAINER' exists on '$STORAGE_ACCOUNT'..."
az storage container create \
  --account-name "$STORAGE_ACCOUNT" \
  --name "$CONTAINER" \
  --auth-mode login \
  --only-show-errors > /dev/null

echo "Uploading seed JSON..."
az storage blob upload-batch \
  --account-name "$STORAGE_ACCOUNT" \
  --destination "$CONTAINER" \
  --source "$SEED_DIR" \
  --pattern '*.json' \
  --overwrite \
  --auth-mode login \
  --only-show-errors

echo "Seed upload complete. The worker's next refresh tick (≤5 min) will pick it up."
