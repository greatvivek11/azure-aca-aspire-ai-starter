#!/usr/bin/env bash
# Ensures a local SQL Server connection profile exists in .vscode/settings.json for the mssql extension.
# Idempotent: safe to run on every folder open.
set -euo pipefail

info() { echo "[info] $*"; }
warn() { echo "[warn] $*" >&2; }

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
aspire_env_path="$repo_root/src/aspire/.env"

# In remote/devcontainer sessions, VS Code reads machine settings from
# ~/.vscode-server/data/Machine/settings.json.
if [[ -d "$HOME/.vscode-server/data/Machine" ]]; then
  settings_dir="$HOME/.vscode-server/data/Machine"
else
  case "$(uname)" in
    Darwin)
      settings_dir="$HOME/Library/Application Support/Code/User"
      ;;
    *)
      settings_dir="$HOME/.config/Code/User"
      ;;
  esac
fi

settings_path="$settings_dir/settings.json"

mkdir -p "$settings_dir"

if [[ ! -f "$settings_path" ]]; then
  printf '{\n}\n' > "$settings_path"
fi

if command -v code >/dev/null 2>&1; then
  if ! code --list-extensions 2>/dev/null | grep -qi '^ms-mssql\.mssql$'; then
    info "ms-mssql.mssql extension is not installed yet; skipping SQL profile bootstrap"
    exit 0
  fi
else
  info "VS Code CLI not found on PATH; continuing SQL profile bootstrap"
fi

sql_host_port="1433"

if [[ -f "$aspire_env_path" ]]; then
  env_port_line="$(grep -E '^\s*SQL_HOST_PORT\s*=\s*[0-9]+\s*$' "$aspire_env_path" | head -n 1 || true)"
  if [[ -n "$env_port_line" ]]; then
    sql_host_port="$(echo "$env_port_line" | sed -E 's/^\s*SQL_HOST_PORT\s*=\s*([0-9]+)\s*$/\1/')"
  fi
fi

if command -v docker >/dev/null 2>&1; then
  localhost_port_regex='127\.0\.0\.1:([0-9]+)->1433/tcp'
  any_host_port_regex=':([0-9]+)->1433/tcp'
  while IFS='|' read -r name ports; do
    [[ "$name" == sql-* ]] || continue
    if [[ "$ports" =~ $localhost_port_regex ]]; then
      sql_host_port="${BASH_REMATCH[1]}"
      break
    fi
    if [[ "$ports" =~ $any_host_port_regex ]]; then
      sql_host_port="${BASH_REMATCH[1]}"
      break
    fi
  done < <(docker ps --filter "name=sql" --format '{{.Names}}|{{.Ports}}' 2>/dev/null || true)
fi

server="127.0.0.1"
database="AcaAspireAiTemplate"
user="sa"
password="P@ssw0rd"

node - "$settings_path" "$sql_host_port" "$server" "$database" "$user" "$password" <<'NODE'
const fs = require('fs');

const [settingsPath, portRaw, server, database, user, password] = process.argv.slice(2);
const port = Number(portRaw);
const profileName = `mssql-container-${port}`;

let root = {};
try {
  const content = fs.readFileSync(settingsPath, 'utf8').trim();
  root = content ? JSON.parse(content) : {};
} catch {
  root = {};
}

root['mssql.connections'] = [
  {
    id: `mssql-container-localhost-${port}`,
    groupId: 'ROOT',
    server,
    port,
    database,
    authenticationType: 'SqlLogin',
    user,
    password,
    connectionString: `Server=${server},${port};Database=${database};User ID=${user};Password=${password};Encrypt=False;TrustServerCertificate=True;Connection Timeout=15`,
    encrypt: 'Optional',
    trustServerCertificate: true,
    emptyPasswordInput: false,
    savePassword: true,
    profileName
  }
];

fs.writeFileSync(settingsPath, `${JSON.stringify(root, null, 2)}\n`, 'utf8');
NODE

info "SQL connection profile ensured in VS Code user settings (server=$server, profile=mssql-container-$sql_host_port)"
exit 0
