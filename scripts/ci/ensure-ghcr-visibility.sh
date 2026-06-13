#!/usr/bin/env bash
set -euo pipefail

owner="${GITHUB_REPOSITORY_OWNER}"
repo_lower="$(basename "${GITHUB_REPOSITORY}" | tr '[:upper:]' '[:lower:]')"
owner_type="$(curl -sS -H "Authorization: Bearer ${GH_TOKEN}" -H "Accept: application/vnd.github+json" "https://api.github.com/users/${owner}" | jq -r '.type')"

if [[ "${owner_type}" == "Organization" ]]; then
  scope_path="orgs/${owner}"
else
  scope_path="users/${owner}"
fi

packages=(
  "${repo_lower}-backend"
  "${repo_lower}-frontend"
  "${repo_lower}-worker"
)

for package_name in "${packages[@]}"; do
  visibility_url="https://api.github.com/${scope_path}/packages/container/${package_name}/visibility"
  package_url="https://api.github.com/${scope_path}/packages/container/${package_name}"

  status="$(curl -sS -o /tmp/ghcr_visibility_body.txt -w "%{http_code}" \
    -X PATCH \
    -H "Authorization: Bearer ${GH_TOKEN}" \
    -H "Accept: application/vnd.github+json" \
    "${visibility_url}" \
    -d '{"visibility":"public"}')"

  if [[ "${status}" != "204" && "${status}" != "422" ]]; then
    echo "GHCR visibility update for ${package_name} returned HTTP ${status}."
  fi

  visibility=""
  retries=5
  while (( retries > 0 )); do
    visibility="$(curl -sS \
      -H "Authorization: Bearer ${GH_TOKEN}" \
      -H "Accept: application/vnd.github+json" \
      "${package_url}" | jq -r '.visibility // empty')"

    if [[ -n "${visibility}" ]]; then
      break
    fi
    retries=$((retries - 1))
    sleep 2
  done

  if [[ "${visibility}" != "public" ]]; then
    if [[ -z "${EXTERNAL_REGISTRY_USERNAME:-}" || -z "${EXTERNAL_REGISTRY_PASSWORD:-}" ]]; then
      echo "GHCR package '${package_name}' is not public and no registry credentials were provided."
      echo "Either make GHCR packages public or set EXTERNAL_REGISTRY_USERNAME/EXTERNAL_REGISTRY_PASSWORD so ACA can authenticate."
      exit 1
    fi
  fi
done
