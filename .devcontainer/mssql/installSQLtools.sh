#!/bin/bash
set -euo pipefail

echo "Installing mssql-tools"

# Use a dedicated keyring for Microsoft packages instead of apt-key.
mkdir -p /etc/apt/keyrings
curl -sSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /etc/apt/keyrings/microsoft.gpg
chmod 0644 /etc/apt/keyrings/microsoft.gpg

DISTRO=$(lsb_release -is | tr '[:upper:]' '[:lower:]')
CODENAME=$(lsb_release -cs)
ARCH=$(dpkg --print-architecture)

echo "deb [arch=${ARCH} signed-by=/etc/apt/keyrings/microsoft.gpg] https://packages.microsoft.com/repos/microsoft-${DISTRO}-${CODENAME}-prod ${CODENAME} main" > /etc/apt/sources.list.d/microsoft.list

# Some base images may carry a stale Yarn apt source that fails signature validation
# and blocks all apt operations. Retry once after removing that optional source.
if ! apt-get update; then
	echo "apt-get update failed; removing stale Yarn apt source and retrying"
	rm -f /etc/apt/sources.list.d/yarn.list /etc/apt/sources.list.d/yarn*.list
	apt-get update
fi

if ACCEPT_EULA=Y apt-get -y install unixodbc-dev msodbcsql17 libunwind8 mssql-tools18; then
	echo "Installed SQL tooling packages for architecture: ${ARCH}"
else
	echo "Warning: SQL tooling packages were not available for architecture: ${ARCH}. Continuing without sqlcmd tooling."
fi

echo "Installing sqlpackage"
curl -sSL -o sqlpackage.zip "https://aka.ms/sqlpackage-linux"
mkdir -p /opt/sqlpackage
unzip -o sqlpackage.zip -d /opt/sqlpackage >/dev/null
rm sqlpackage.zip
chmod a+x /opt/sqlpackage/sqlpackage
