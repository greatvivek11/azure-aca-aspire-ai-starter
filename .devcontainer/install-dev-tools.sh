#!/usr/bin/env bash
set -euo pipefail

SUDO=""
if [[ "$(id -u)" -ne 0 ]]; then
  SUDO="sudo"
fi

if ! command -v az >/dev/null 2>&1; then
  echo "Installing Azure CLI..."
  curl -sL https://aka.ms/InstallAzureCLIDeb | $SUDO bash
else
  echo "Azure CLI already installed."
fi

if ! command -v azd >/dev/null 2>&1; then
  echo "Installing Azure Developer CLI (azd)..."
  curl -fsSL https://aka.ms/install-azd.sh | bash
else
  echo "Azure Developer CLI already installed."
fi

if ! dotnet tool list -g | awk '{print $1}' | grep -qx "aspire.cli"; then
  echo "Installing Aspire CLI..."
  dotnet tool install -g aspire.cli
else
  echo "Updating Aspire CLI..."
  dotnet tool update -g aspire.cli
fi

echo "Dev tools installation complete."
