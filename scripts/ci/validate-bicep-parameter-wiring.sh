#!/usr/bin/env bash
set -euo pipefail

bicep_params_file="/tmp/bicep_params.txt"
paramfile_keys_file="/tmp/paramfile_keys.txt"

awk '/^param[[:space:]]+[A-Za-z0-9_]+[[:space:]]+/{print $2}' infra/main.bicep | sort -u > "${bicep_params_file}"
jq -r '.parameters | keys[]' infra/main.parameters.json | sort -u > "${paramfile_keys_file}"

missing="$(comm -23 "${bicep_params_file}" "${paramfile_keys_file}" || true)"
extra="$(comm -13 "${bicep_params_file}" "${paramfile_keys_file}" || true)"

if [[ -n "${missing}" || -n "${extra}" ]]; then
  echo "Bicep/parameters wiring mismatch detected."
  if [[ -n "${missing}" ]]; then
    echo "Missing in infra/main.parameters.json:" 
    echo "${missing}"
  fi
  if [[ -n "${extra}" ]]; then
    echo "Extra keys in infra/main.parameters.json not present in infra/main.bicep:"
    echo "${extra}"
  fi
  exit 1
fi

echo "Bicep/parameters wiring check passed."
