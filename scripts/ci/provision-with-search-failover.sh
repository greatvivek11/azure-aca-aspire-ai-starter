#!/usr/bin/env bash
set -euo pipefail

mode_lower="$(echo "${AZURE_SEARCH_PROVISIONING_MODE:-provision}" | tr '[:upper:]' '[:lower:]')"
failover_enabled="$(echo "${AZURE_SEARCH_FAILOVER_ENABLED:-false}" | tr '[:upper:]' '[:lower:]')"

normalize_region() {
  echo "$1" | tr '[:upper:]' '[:lower:]' | tr -d '[:space:]'
}

declare -a candidates=()
add_candidate() {
  local region
  region="$(normalize_region "$1")"
  if [[ -z "${region}" ]]; then
    return
  fi
  local existing
  for existing in "${candidates[@]}"; do
    if [[ "${existing}" == "${region}" ]]; then
      return
    fi
  done
  candidates+=("${region}")
}

primary_search_location="${AZURE_SEARCH_LOCATION:-${AZURE_LOCATION}}"
add_candidate "${primary_search_location}"

if [[ "${mode_lower}" != "provision" || "${failover_enabled}" != "true" ]]; then
  echo "Search failover disabled or not applicable. Using AZURE_SEARCH_LOCATION='${candidates[0]}'."
  azd env set AZURE_SEARCH_LOCATION "${candidates[0]}"
  azd provision --no-prompt
  echo "EFFECTIVE_SEARCH_LOCATION=${candidates[0]}" >> "$GITHUB_ENV"
  exit 0
fi

if [[ -n "${AZURE_SEARCH_FALLBACK_LOCATIONS:-}" ]]; then
  IFS=',' read -r -a configured <<< "${AZURE_SEARCH_FALLBACK_LOCATIONS}"
  for region in "${configured[@]}"; do
    add_candidate "${region}"
  done
else
  case "${candidates[0]}" in
    eastus2)
      add_candidate eastus
      add_candidate centralus
      add_candidate westus3
      ;;
    eastus)
      add_candidate eastus2
      add_candidate centralus
      add_candidate westus3
      ;;
    southindia)
      add_candidate centralindia
      add_candidate westindia
      add_candidate southeastasia
      ;;
    westeurope)
      add_candidate northeurope
      add_candidate uksouth
      add_candidate francecentral
      ;;
    *)
      ;;
  esac
fi

echo "Search location attempts (ordered): ${candidates[*]}"

for region in "${candidates[@]}"; do
  echo "Attempting provision with AZURE_SEARCH_LOCATION='${region}'"
  azd env set AZURE_SEARCH_LOCATION "${region}"

  log_file="$(mktemp)"
  set +e
  azd provision --no-prompt 2>&1 | tee "${log_file}"
  exit_code="${PIPESTATUS[0]}"
  set -e

  if [[ "${exit_code}" -eq 0 ]]; then
    echo "Provision succeeded with AZURE_SEARCH_LOCATION='${region}'"
    echo "EFFECTIVE_SEARCH_LOCATION=${region}" >> "$GITHUB_ENV"
    exit 0
  fi

  if grep -q "InsufficientResourcesAvailable" "${log_file}"; then
    echo "Capacity exhausted in '${region}'. Trying next configured region."
    continue
  fi

  echo "Provision failed for a non-capacity reason in '${region}'. Stopping failover."
  exit "${exit_code}"
done

echo "Provision failed due to Search capacity in all configured regions."
exit 1
