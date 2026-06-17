#!/usr/bin/env bash
set -euo pipefail

mode_lower="$(echo "${AZURE_SEARCH_PROVISIONING_MODE:-provision}" | tr '[:upper:]' '[:lower:]')"
failover_enabled="$(echo "${AZURE_SEARCH_FAILOVER_ENABLED:-false}" | tr '[:upper:]' '[:lower:]')"
ai_mode_lower="$(echo "${AI_SERVICES_PROVISIONING_MODE:-provision}" | tr '[:upper:]' '[:lower:]')"

normalize_bool() {
  local value
  value="$(echo "${1:-false}" | tr '[:upper:]' '[:lower:]' | tr -d '[:space:]')"
  if [[ "${value}" == "true" ]]; then
    echo "true"
    return
  fi
  echo "false"
}

is_retryable_ai_services_conflict() {
  local log_file="$1"

  grep -q "RequestConflict: Cannot modify resource with id '.*/Microsoft\.CognitiveServices/accounts/.*provisioning state is not terminal" "${log_file}"
}

is_soft_deleted_ai_services_restore_required() {
  local log_file="$1"

  grep -q "FlagMustBeSetForRestore" "${log_file}" && \
    grep -q "Microsoft\.CognitiveServices/accounts" "${log_file}"
}

extract_soft_deleted_ai_account_name() {
  local log_file="$1"

  grep -Eo "Microsoft\.CognitiveServices/accounts/[a-z0-9-]+" "${log_file}" \
    | awk -F/ '{print $NF}' \
    | head -n1
}

run_azd_provision() {
  local region="$1"
  local max_attempts=4
  local attempt exit_code
  local restore_already_enabled

  restore_already_enabled="$(normalize_bool "${AZURE_AI_SERVICES_RESTORE:-false}")"

  for attempt in $(seq 1 "${max_attempts}"); do
    local log_file
    log_file="$(mktemp)"

    set +e
    azd provision --no-prompt 2>&1 | tee "${log_file}"
    exit_code="${PIPESTATUS[0]}"
    set -e

    if [[ "${exit_code}" -eq 0 ]]; then
      echo "Provision succeeded with AZURE_SEARCH_LOCATION='${region}'"
      echo "EFFECTIVE_SEARCH_LOCATION=${region}" >> "$GITHUB_ENV"
      return 0
    fi

    if is_retryable_ai_services_conflict "${log_file}"; then
      if [[ "${attempt}" -lt "${max_attempts}" ]]; then
        local retry_delay
        retry_delay=$((attempt * 30))
        echo "Azure AI Services account is still transitioning. Retrying azd provision in ${retry_delay}s (attempt ${attempt}/${max_attempts})..."
        sleep "${retry_delay}"
        continue
      fi

      echo "Azure AI Services account never reached a terminal provisioning state after ${max_attempts} attempts."
      return "${exit_code}"
    fi

    if [[ "${ai_mode_lower}" == "provision" ]] && is_soft_deleted_ai_services_restore_required "${log_file}"; then
      if [[ "${restore_already_enabled}" != "true" ]]; then
        local soft_deleted_account
        soft_deleted_account="$(extract_soft_deleted_ai_account_name "${log_file}")"
          if [[ -n "${soft_deleted_account}" ]]; then
            echo "Detected soft-deleted Azure AI Services account '${soft_deleted_account}'; enabling AZURE_AI_SERVICES_RESTORE=true and retrying."
          else
            echo "Detected soft-deleted Azure AI Services account; enabling AZURE_AI_SERVICES_RESTORE=true and retrying."
          fi
        AZURE_AI_SERVICES_RESTORE="true"
        restore_already_enabled="true"
        azd env set AZURE_AI_SERVICES_RESTORE "true"
        continue
      fi

      echo "Soft-delete restore was already enabled but the deployment still failed for Azure AI Services account restore."
      return "${exit_code}"
    fi

    if grep -q "InsufficientResourcesAvailable" "${log_file}"; then
      echo "Capacity exhausted in '${region}'. Trying next configured region."
      return 10
    fi

    echo "Provision failed for a non-retryable reason in '${region}'. Stopping failover."
    return "${exit_code}"
  done
}

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

AZURE_AI_SERVICES_RESTORE="$(normalize_bool "${AZURE_AI_SERVICES_RESTORE:-false}")"
azd env set AZURE_AI_SERVICES_RESTORE "${AZURE_AI_SERVICES_RESTORE}"
echo "Using AZURE_AI_SERVICES_RESTORE='${AZURE_AI_SERVICES_RESTORE}'."

if [[ "${mode_lower}" != "provision" || "${failover_enabled}" != "true" ]]; then
  echo "Search failover disabled or not applicable. Using AZURE_SEARCH_LOCATION='${candidates[0]}'."
  azd env set AZURE_SEARCH_LOCATION "${candidates[0]}"
  run_azd_provision "${candidates[0]}"
  exit $?
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

  if run_azd_provision "${region}"; then
    exit 0
  fi

  exit_code="$?"
  if [[ "${exit_code}" -eq 10 ]]; then
    continue
  fi

  exit "${exit_code}"
done

echo "Provision failed due to Search capacity in all configured regions."
exit 1
