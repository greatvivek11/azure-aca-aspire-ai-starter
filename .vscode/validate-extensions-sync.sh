#!/usr/bin/env bash
set -u

# Validate that .devcontainer/devcontainer.json extensions list stays in sync with
# .vscode/extensions.json (single source of truth).
# This ensures all team members get consistent extension recommendations.

EXTENSIONS_JSON=".vscode/extensions.json"
DEVCONTAINER_JSON=".devcontainer/devcontainer.json"

if [[ ! -f "$EXTENSIONS_JSON" ]]; then
  echo "[error] $EXTENSIONS_JSON not found"
  exit 1
fi

if [[ ! -f "$DEVCONTAINER_JSON" ]]; then
  echo "[error] $DEVCONTAINER_JSON not found"
  exit 1
fi

# Extract extensions from source of truth.
extensions_from_source=$(jq -r '.recommendations[]' "$EXTENSIONS_JSON" 2>/dev/null | sort)

# Extract extensions from devcontainer config.
extensions_from_devcontainer=$(jq -r '.customizations.vscode.extensions[]' "$DEVCONTAINER_JSON" 2>/dev/null | sort)

# Compare.
if [[ "$extensions_from_source" != "$extensions_from_devcontainer" ]]; then
  echo "[error] Extension lists are out of sync!"
  echo ""
  echo "Source of truth (.vscode/extensions.json):"
  echo "$extensions_from_source"
  echo ""
  echo "DevContainer config (.devcontainer/devcontainer.json):"
  echo "$extensions_from_devcontainer"
  echo ""
  echo "Action: Update .devcontainer/devcontainer.json customizations.vscode.extensions"
  echo "        to match .vscode/extensions.json recommendations array."
  exit 1
fi

echo "[ok] Extension lists are in sync."
exit 0
