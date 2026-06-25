#!/usr/bin/env bash
# Cross-platform setup entry point: detects OS and runs the appropriate setup script
# Usage: bash scripts/setup.sh [--validate-only]

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

# Parse arguments
VALIDATE_ONLY="${VALIDATE_ONLY:-false}"
for arg in "$@"; do
  case "$arg" in
    --validate-only)
      VALIDATE_ONLY="true"
      ;;
  esac
done

# Detect OS
OS_TYPE="$(uname -s)"
case "$OS_TYPE" in
  Darwin)
    OS="macos"
    ;;
  Linux)
    OS="linux"
    ;;
  MINGW64_NT*|MSYS_NT*)
    OS="windows"
    ;;
  *)
    echo "[error] Unsupported operating system: $OS_TYPE"
    echo "[info] Supported: macOS (Darwin), Linux, Windows (MINGW64/MSYS)"
    exit 1
    ;;
esac

echo "[setup] Detected OS: $OS ($OS_TYPE)"

case "$OS" in
  windows)
    # On Windows, use PowerShell script
    if command -v powershell >/dev/null 2>&1; then
      echo "[setup] Running Windows setup via PowerShell..."
      if [[ "$VALIDATE_ONLY" == "true" ]]; then
        powershell -ExecutionPolicy Bypass -File "${SCRIPT_DIR}/setup-env.ps1" -ValidateOnly
      else
        powershell -ExecutionPolicy Bypass -File "${SCRIPT_DIR}/setup-env.ps1"
      fi
    else
      echo "[error] PowerShell not found. Please install PowerShell or use WSL for setup."
      exit 1
    fi
    ;;
  macos|linux)
    # On Unix-like systems, use bash script
    echo "[setup] Running Unix-like setup via bash..."
    if [[ "$VALIDATE_ONLY" == "true" ]]; then
      VALIDATE_ONLY="true" bash "${SCRIPT_DIR}/setup-dev-tools.sh"
    else
      bash "${SCRIPT_DIR}/setup-dev-tools.sh"
    fi
    ;;
esac

exit $?
