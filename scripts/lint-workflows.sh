#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
TOOLS_DIR="${REPO_ROOT}/.tools/actionlint"
ACTIONLINT_VERSION="${ACTIONLINT_VERSION:-1.7.12}"

resolve_platform() {
  local os arch

  os="$(uname -s)"
  arch="$(uname -m)"

  case "${os}" in
    Linux) os="linux" ;;
    Darwin) os="darwin" ;;
    *)
      echo "Unsupported OS for actionlint: ${os}" >&2
      exit 1
      ;;
  esac

  case "${arch}" in
    x86_64|amd64) arch="amd64" ;;
    arm64|aarch64) arch="arm64" ;;
    *)
      echo "Unsupported architecture for actionlint: ${arch}" >&2
      exit 1
      ;;
  esac

  echo "${os}" "${arch}"
}

ensure_actionlint() {
  if [[ -n "${ACTIONLINT_BIN:-}" ]]; then
    if [[ ! -x "${ACTIONLINT_BIN}" ]]; then
      echo "ACTIONLINT_BIN is set but not executable: ${ACTIONLINT_BIN}" >&2
      exit 1
    fi
    echo "${ACTIONLINT_BIN}"
    return 0
  fi

  if command -v actionlint >/dev/null 2>&1; then
    echo "actionlint"
    return 0
  fi

  local os arch archive url
  read -r os arch < <(resolve_platform)

  mkdir -p "${TOOLS_DIR}"
  archive="${TOOLS_DIR}/actionlint_${ACTIONLINT_VERSION}_${os}_${arch}.tar.gz"
  url="https://github.com/rhysd/actionlint/releases/download/v${ACTIONLINT_VERSION}/actionlint_${ACTIONLINT_VERSION}_${os}_${arch}.tar.gz"

  if [[ ! -x "${TOOLS_DIR}/actionlint" ]]; then
    echo "Downloading actionlint v${ACTIONLINT_VERSION}..." >&2
    curl -fsSL "${url}" -o "${archive}"
    tar -xzf "${archive}" -C "${TOOLS_DIR}" actionlint
    chmod +x "${TOOLS_DIR}/actionlint"
  fi

  echo "${TOOLS_DIR}/actionlint"
}

main() {
  local actionlint_bin
  actionlint_bin="$(ensure_actionlint)"

  local workflow_dir="${REPO_ROOT}/.github/workflows"
  local workflow_files=()
  while IFS= read -r file; do
    workflow_files+=("${file}")
  done < <(find "${workflow_dir}" -maxdepth 1 -type f \( -name "*.yml" -o -name "*.yaml" \) | sort)

  if [[ "${#workflow_files[@]}" -eq 0 ]]; then
    echo "No workflow files found under ${workflow_dir}"
    exit 0
  fi

  "${actionlint_bin}" -oneline "${workflow_files[@]}"

  local shell_script_files=()
  while IFS= read -r file; do
    shell_script_files+=("${file#${REPO_ROOT}/}")
  done < <(find "${REPO_ROOT}/scripts/ci" -maxdepth 1 -type f -name "*.sh" | sort)

  if [[ "${#shell_script_files[@]}" -gt 0 ]]; then
    if command -v shellcheck >/dev/null 2>&1; then
      (cd "${REPO_ROOT}" && shellcheck -x "${shell_script_files[@]}")
    elif command -v docker >/dev/null 2>&1; then
      echo "shellcheck is not installed locally; using Docker image koalaman/shellcheck:stable" >&2
      (
        cd "${REPO_ROOT}"
        docker run --rm -v "${REPO_ROOT}:/mnt" koalaman/shellcheck:stable -x "${shell_script_files[@]}"
      )
    else
      echo "shellcheck is not installed and Docker is unavailable; skipping shell script linting." >&2
    fi
  fi
}

main "$@"
