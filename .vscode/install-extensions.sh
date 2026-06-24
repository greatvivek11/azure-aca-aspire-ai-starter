#!/usr/bin/env bash
set -u

# Install required workspace extensions from .vscode/extensions.json (single source of truth).
# This script is idempotent: reinstall attempts are safe.
# Works on macOS, Linux, and WSL (no admin rights required).

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EXTENSIONS_JSON="${SCRIPT_DIR}/extensions.json"

if [[ ! -f "$EXTENSIONS_JSON" ]]; then
  echo "[error] extensions.json not found at $EXTENSIONS_JSON"
  exit 1
fi

# Find 'code' CLI in PATH or common VS Code install locations
CODE_BIN="code"
if ! command -v code >/dev/null 2>&1; then
  # Try common macOS installation path
  if [[ -x "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code" ]]; then
    CODE_BIN="/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code"
  elif [[ -x "/usr/bin/code" ]]; then
    CODE_BIN="/usr/bin/code"
  elif [[ -x "/usr/local/bin/code" ]]; then
    CODE_BIN="/usr/local/bin/code"
  else
    echo "[error] 'code' CLI not found. Ensure VS Code is installed and 'code' is in PATH."
    echo "[info] On macOS: Run 'Cmd+Shift+P' > 'Shell Command: Install code command in PATH'"
    echo "[info] On Linux: Ensure VS Code /usr/bin/code is in PATH"
    exit 1
  fi
fi

# Check if jq is available for JSON parsing; fallback to grep if not
if command -v jq >/dev/null 2>&1; then
  extensions=($(jq -r '.recommendations[]' "$EXTENSIONS_JSON" 2>/dev/null || echo ""))
else
  # Fallback: simple grep-based extraction (works when jq unavailable)
  extensions=($(grep -o '"[^"]*"' "$EXTENSIONS_JSON" | grep -v -E '^\s*"(recommendations|unwantedRecommendations)"\s*$' | sed 's/"//g' | sort -u))
fi

if [[ ${#extensions[@]} -eq 0 ]]; then
  echo "[error] No extensions found in extensions.json"
  exit 1
fi

echo "[info] Installing ${#extensions[@]} required VS Code extensions..."
failed_count=0
for ext in "${extensions[@]}"; do
  [[ -z "$ext" ]] && continue  # Skip empty strings
  echo "Installing ${ext}..."
  if ! "${CODE_BIN}" --install-extension "${ext}" >/dev/null 2>&1; then
    echo "[warn] Failed to install ${ext}"
    ((failed_count++))
  fi
done

if [[ $failed_count -gt 0 ]]; then
  echo "[warn] $failed_count extension(s) failed to install"
  echo "[info] This may happen if VS Code is running; close and retry."
  exit 1
else
  echo "[info] Extension bootstrap completed successfully."
  exit 0
fi
