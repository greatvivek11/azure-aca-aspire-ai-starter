#!/usr/bin/env bash
set -euo pipefail

# Idempotent fallback extension installer for devcontainers.
# Parses .vscode/extensions.json as single source of truth (single source of truth).
# Uses VS Code Server CLI when the `code` shim is unavailable.

# Parse extensions from the host's extensions.json mounted at /workspaces/app.
EXTENSIONS_JSON="/workspaces/app/.vscode/extensions.json"
if [[ ! -f "$EXTENSIONS_JSON" ]]; then
  echo "[warn] extensions.json not found; cannot load extension list"
  exit 0
fi

# Extract recommendations array from extensions.json.
declare -a EXTENSIONS
mapfile -t EXTENSIONS < <(jq -r '.recommendations[]' "$EXTENSIONS_JSON" 2>/dev/null || echo "")

if [[ ${#EXTENSIONS[@]} -eq 0 ]]; then
  echo "[warn] No extensions found in extensions.json"
  exit 0
fi

MARKER_FILE="$HOME/.cache/devcontainer-vscode-extensions-v1"

if [[ -f "$MARKER_FILE" ]]; then
  echo "[info] VS Code extensions already bootstrapped for this container."
  exit 0
fi

install_cli=""
if command -v code >/dev/null 2>&1 && code --version >/dev/null 2>&1; then
  install_cli="code"
else
  install_cli="$(find /vscode/vscode-server/bin -type f -name code-server 2>/dev/null | head -n 1 || true)"
fi

if [[ -z "$install_cli" ]]; then
  echo "[warn] Could not find a VS Code CLI for extension installation."
  echo "[warn] Run 'Dev Containers: Rebuild Container' and retry."
  exit 0
fi

echo "[info] Installing missing VS Code extensions using: $install_cli"
installed="$("$install_cli" --list-extensions 2>/dev/null || true)"

for ext in "${EXTENSIONS[@]}"; do
  if echo "$installed" | grep -qi "^${ext}$"; then
    echo "[info] $ext already installed"
    continue
  fi

  if "$install_cli" --install-extension "$ext" >/dev/null 2>&1; then
    echo "[ok] Installed $ext"
  else
    echo "[warn] Failed to install $ext"
  fi

done

mkdir -p "$(dirname "$MARKER_FILE")"
date > "$MARKER_FILE"
echo "[info] Extension bootstrap completed."
