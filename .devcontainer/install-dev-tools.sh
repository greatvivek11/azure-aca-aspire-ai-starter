#!/usr/bin/env bash
set -euo pipefail

SUDO=""
if [[ "$(id -u)" -ne 0 ]]; then
  SUDO="sudo"
fi

# Retry a command a few times to survive transient network failures
# (DNS hiccups, GitHub/apt/NuGet mirror timeouts). Keeps setup resilient so a
# single flaky download never aborts the rest of container provisioning.
# Usage: retry <attempts> <delay_seconds> <description> <command> [args...]
retry() {
  local max="$1" delay="$2" desc="$3"
  shift 3
  local attempt=1
  until "$@"; do
    if (( attempt >= max )); then
      echo "[warn] ${desc} failed after ${attempt} attempts."
      return 1
    fi
    echo "[warn] ${desc} failed (attempt ${attempt}/${max}). Retrying in ${delay}s..."
    sleep "$delay"
    attempt=$(( attempt + 1 ))
  done
  return 0
}

# Wrappers so piped installers can be retried as a single unit.
install_az()  { curl -sL https://aka.ms/InstallAzureCLIDeb | $SUDO bash; }
install_azd() { curl -fsSL https://aka.ms/install-azd.sh | bash; }

# --- Azure CLI -------------------------------------------------------------
if ! command -v az >/dev/null 2>&1; then
  echo "Installing Azure CLI..."
  retry 3 5 "Azure CLI install" install_az \
    || echo "[warn] Continuing without Azure CLI. Retry later: curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash"
else
  echo "Azure CLI already installed."
fi

# --- Azure Developer CLI (azd) --------------------------------------------
if ! command -v azd >/dev/null 2>&1; then
  echo "Installing Azure Developer CLI (azd)..."
  retry 3 5 "azd install" install_azd \
    || echo "[warn] Continuing without azd. Retry later: curl -fsSL https://aka.ms/install-azd.sh | bash"
else
  echo "Azure Developer CLI already installed."
fi

# --- Aspire CLI ------------------------------------------------------------
if ! dotnet tool list -g | awk '{print $1}' | grep -qx "aspire.cli"; then
  echo "Installing Aspire CLI..."
  retry 3 5 "Aspire CLI install" dotnet tool install -g aspire.cli \
    || echo "[warn] Continuing without Aspire CLI. Retry later: dotnet tool install -g aspire.cli"
else
  echo "Updating Aspire CLI..."
  retry 3 5 "Aspire CLI update" dotnet tool update -g aspire.cli \
    || echo "[warn] Aspire CLI update failed. Retry later: dotnet tool update -g aspire.cli"
fi

# --- Dapr runtime (slim mode = local binaries only, no Docker dependencies) -
# Binaries come from GitHub releases, which can intermittently time out.
if [[ -x "$HOME/.dapr/bin/daprd" ]]; then
  echo "Dapr runtime already initialized."
else
  echo "Initializing Dapr runtime (slim)..."
  retry 3 5 "Dapr init" dapr init --slim \
    || echo "[warn] Dapr init failed. Retry later: dapr uninstall --all && dapr init --slim"
fi

# --- Frontend dependencies -------------------------------------------------
FRONTEND_NODE_MODULES_DIR="src/frontend/node_modules"
echo "Preparing frontend node_modules directory..."
mkdir -p "$FRONTEND_NODE_MODULES_DIR"

# Docker named volumes can be root-owned on first mount; fix ownership for npm.
if ! touch "$FRONTEND_NODE_MODULES_DIR/.write-test" 2>/dev/null; then
  echo "[setup] Fixing permissions for $FRONTEND_NODE_MODULES_DIR"
  $SUDO chown -R "$(id -u):$(id -g)" "$FRONTEND_NODE_MODULES_DIR"
fi
rm -f "$FRONTEND_NODE_MODULES_DIR/.write-test"

echo "Installing frontend npm dependencies..."
if ! retry 3 5 "npm ci (frontend)" npm ci --prefix src/frontend --include=optional; then
  echo "[warn] npm ci failed. Falling back to npm install to recover optional native packages."
  retry 3 5 "npm install (frontend)" npm install --prefix src/frontend --include=optional \
    || echo "[warn] Frontend dependency install failed. Retry later: npm ci --prefix src/frontend --include=optional"
fi

echo "Restoring .NET solution packages..."
retry 3 5 ".NET solution restore" dotnet restore azure-aca-aspire-ai-starter.sln \
  || echo "[warn] .NET solution restore failed. Retry later: dotnet restore azure-aca-aspire-ai-starter.sln"

echo "Dev tools installation complete."
