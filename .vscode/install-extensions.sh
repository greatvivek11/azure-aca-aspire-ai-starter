#!/usr/bin/env bash
set -u

# Install required workspace extensions from .vscode/extensions.json (single source of truth).
# This script is idempotent: reinstall attempts are safe.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EXTENSIONS_JSON="${SCRIPT_DIR}/extensions.json"

if [[ ! -f "$EXTENSIONS_JSON" ]]; then
  echo "[error] extensions.json not found at $EXTENSIONS_JSON"
  exit 1
fi

CODE_BIN="code"
if [[ -x "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code" ]]; then
  CODE_BIN="/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code"
fi

# Parse extensions from extensions.json recommendations array.
extensions=($(jq -r '.recommendations[]' "$EXTENSIONS_JSON" 2>/dev/null || echo ""))

if [[ ${#extensions[@]} -eq 0 ]]; then
  echo "[error] No extensions found in extensions.json"
  exit 1
fi

echo "[info] Installing ${#extensions[@]} required VS Code extensions..."
for ext in "${extensions[@]}"; do
  echo "Installing ${ext}..."
  "${CODE_BIN}" --install-extension "${ext}" >/dev/null 2>&1 || echo "[warn] Failed to install ${ext}"
done

echo "[info] Extension bootstrap completed."
