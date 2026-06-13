#!/usr/bin/env bash
set -euo pipefail

bash scripts/validate-azure-env.sh \
  "${AZURE_SUBSCRIPTION_ID}" \
  "${DEPLOY_RESOURCE_GROUP}" \
  "${AZURE_LOCATION}"
