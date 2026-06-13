#!/usr/bin/env bash
set -euo pipefail

mode="${1:-}"

case "${mode}" in
  sql-admin-login)
    mode_lower="$(echo "${SQL_PROVISIONING_MODE:-provision}" | tr '[:upper:]' '[:lower:]')"

    if [[ "${mode_lower}" == "existing" ]]; then
      echo "SQL_PROVISIONING_MODE=existing; skipping SQL admin credential validation."
      exit 0
    fi

    if [ -z "${AZURE_SQL_ADMIN_LOGIN:-}" ]; then
      echo "AZURE_SQL_ADMIN_LOGIN secret is required."
      exit 1
    fi

    if [ -z "${AZURE_SQL_ADMIN_PASSWORD:-}" ]; then
      echo "AZURE_SQL_ADMIN_PASSWORD secret is required."
      exit 1
    fi

    login_lower="$(echo "${AZURE_SQL_ADMIN_LOGIN}" | tr '[:upper:]' '[:lower:]')"

    case "${login_lower}" in
      admin|administrator|sa|root|guest|public)
        echo "AZURE_SQL_ADMIN_LOGIN '${AZURE_SQL_ADMIN_LOGIN}' is reserved/invalid for Azure SQL."
        echo "Use a non-reserved value like 'sqladmincopilot'."
        exit 1
        ;;
    esac

    if ! echo "${AZURE_SQL_ADMIN_LOGIN}" | grep -Eq '^[A-Za-z][A-Za-z0-9_]{2,127}$'; then
      echo "AZURE_SQL_ADMIN_LOGIN must start with a letter and contain only letters, digits, or underscore (3-128 chars)."
      exit 1
    fi
    ;;

  sql-provisioning)
    mode_lower="$(echo "${SQL_PROVISIONING_MODE:-provision}" | tr '[:upper:]' '[:lower:]')"

    if [[ "${mode_lower}" != "provision" && "${mode_lower}" != "existing" ]]; then
      echo "SQL_PROVISIONING_MODE must be 'provision' or 'existing'."
      exit 1
    fi

    if [[ "${mode_lower}" == "existing" ]]; then
      if [[ -z "${AZURE_SQL_EXISTING_SERVER_NAME:-}" || -z "${AZURE_SQL_EXISTING_DATABASE_NAME:-}" ]]; then
        echo "When SQL_PROVISIONING_MODE=existing, set AZURE_SQL_EXISTING_SERVER_NAME and AZURE_SQL_EXISTING_DATABASE_NAME."
        exit 1
      fi
    fi

    echo "SQL provisioning mode validated: ${mode_lower}"
    ;;

  search-provisioning)
    mode_lower="$(echo "${AZURE_SEARCH_PROVISIONING_MODE:-provision}" | tr '[:upper:]' '[:lower:]')"

    if [[ "${mode_lower}" != "provision" && "${mode_lower}" != "existing" ]]; then
      echo "AZURE_SEARCH_PROVISIONING_MODE must be 'provision' or 'existing'."
      exit 1
    fi

    if [[ "${mode_lower}" == "existing" && -z "${AZURE_SEARCH_SERVICE_NAME:-}" ]]; then
      echo "When AZURE_SEARCH_PROVISIONING_MODE=existing, set AZURE_SEARCH_SERVICE_NAME."
      exit 1
    fi

    echo "Azure AI Search provisioning mode validated: ${mode_lower}"
    ;;

  search-failover)
    enabled_lower="$(echo "${AZURE_SEARCH_FAILOVER_ENABLED:-false}" | tr '[:upper:]' '[:lower:]')"
    if [[ "${enabled_lower}" != "true" && "${enabled_lower}" != "false" ]]; then
      echo "AZURE_SEARCH_FAILOVER_ENABLED must be 'true' or 'false'."
      exit 1
    fi

    if [[ "${enabled_lower}" == "true" ]]; then
      mode_lower="$(echo "${AZURE_SEARCH_PROVISIONING_MODE:-provision}" | tr '[:upper:]' '[:lower:]')"
      if [[ "${mode_lower}" != "provision" ]]; then
        echo "AZURE_SEARCH_FAILOVER_ENABLED=true requires AZURE_SEARCH_PROVISIONING_MODE=provision."
        exit 1
      fi
    fi

    echo "Azure AI Search failover validated: ${enabled_lower}"
    ;;

  *)
    echo "Unknown validation mode: ${mode}"
    echo "Supported: sql-admin-login, sql-provisioning, search-provisioning, search-failover"
    exit 1
    ;;
esac
