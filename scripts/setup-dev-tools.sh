#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

version_ge() {
  local left_version right_version
  left_version="$1"
  right_version="$2"
  [[ "$(printf '%s\n%s\n' "$right_version" "$left_version" | sort -V | head -n1)" == "$right_version" ]]
}

install_shellcheck() {
  if command -v shellcheck >/dev/null 2>&1; then
    echo "[setup] shellcheck already installed: $(shellcheck --version | head -n 1)"
    return
  fi

  echo "[setup] shellcheck not found; attempting install"
  case "$(uname -s)" in
    Darwin)
      if [[ "${AUTO_INSTALL_SHELLCHECK:-false}" != "true" ]]; then
        echo "[setup] shellcheck missing on macOS. Install manually with: brew install shellcheck"
        echo "[setup] Fallback: scripts/lint-workflows.sh can run shellcheck via Docker when Docker is available."
        return
      fi

      if ! command -v brew >/dev/null 2>&1; then
        echo "[setup] Homebrew not found. Install Homebrew first, then run: brew install shellcheck"
        return
      fi

      NONINTERACTIVE=1 HOMEBREW_NO_AUTO_UPDATE=1 brew install shellcheck || {
        echo "[setup] brew install shellcheck failed. You may need to trust configured taps, then retry."
        echo "[setup] Fallback: scripts/lint-workflows.sh can run shellcheck via Docker when Docker is available."
        return
      }
      ;;
    Linux)
      if command -v apt-get >/dev/null 2>&1; then
        sudo apt-get update
        sudo apt-get install -y shellcheck
      elif command -v dnf >/dev/null 2>&1; then
        sudo dnf install -y ShellCheck
      elif command -v yum >/dev/null 2>&1; then
        sudo yum install -y ShellCheck
      else
        echo "[setup] No supported package manager found. Install shellcheck manually: https://github.com/koalaman/shellcheck"
        return
      fi
      ;;
    *)
      echo "[setup] Unsupported OS for auto shellcheck install. Install manually: https://github.com/koalaman/shellcheck"
      return
      ;;
  esac

  if command -v shellcheck >/dev/null 2>&1; then
    echo "[setup] shellcheck installed: $(shellcheck --version | head -n 1)"
  else
    echo "[setup] shellcheck install attempted, but binary still not on PATH"
  fi
}

main() {
  echo "[setup] repository root: ${ROOT_DIR}"

  if ! command -v dotnet >/dev/null 2>&1; then
    echo "[setup] dotnet SDK is required. Install from https://dotnet.microsoft.com/download"
    exit 1
  fi

  local dotnet_version
  dotnet_version="$(dotnet --version)"
  if ! version_ge "$dotnet_version" "10.0.0"; then
    echo "[setup] dotnet 10.0+ is required (found: ${dotnet_version})"
    exit 1
  fi
  echo "[setup] dotnet version: ${dotnet_version}"

  if ! command -v node >/dev/null 2>&1; then
    echo "[setup] Node.js is required. Install from https://nodejs.org"
    exit 1
  fi

  local node_version
  node_version="$(node --version)"
  if ! version_ge "${node_version#v}" "20.0.0"; then
    echo "[setup] Node.js 20+ is required (found: ${node_version})"
    exit 1
  fi
  echo "[setup] node version: ${node_version}"

  if ! command -v npm >/dev/null 2>&1; then
    echo "[setup] npm is required. Install Node.js from https://nodejs.org"
    exit 1
  fi

  local npm_version
  npm_version="$(npm --version)"
  if ! version_ge "$npm_version" "10.0.0"; then
    echo "[setup] npm 10.0+ is required (found: ${npm_version})"
    exit 1
  fi
  echo "[setup] npm version: ${npm_version}"

  if [[ "${VALIDATE_ONLY:-false}" == "true" ]]; then
    echo "[setup] VALIDATE_ONLY=true; skipping installs, lint, and tests"
    exit 0
  fi

  install_shellcheck

  echo "[setup] ensuring actionlint is available via scripts/lint-workflows.sh"
  bash "${ROOT_DIR}/scripts/lint-workflows.sh" || true

  echo "[setup] restoring .NET solution"
  dotnet restore "${ROOT_DIR}/azure-aca-aspire-ai-starter.sln"

  echo "[setup] installing frontend dependencies"
  npm ci --prefix "${ROOT_DIR}/src/frontend"

  echo "[setup] running frontend lint"
  npm run --prefix "${ROOT_DIR}/src/frontend" lint

  echo "[setup] running backend architecture tests"
  dotnet test "${ROOT_DIR}/src/Backend.Tests/Backend.Tests.csproj"

  echo "[setup] done"
}

main "$@"
