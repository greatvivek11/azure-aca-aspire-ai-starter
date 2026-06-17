#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

echo "[vuln-check] Checking .NET vulnerabilities (backend/worker)"
backend_output="$(dotnet list "$repo_root/src/backend/Backend.csproj" package --vulnerable --include-transitive)"
worker_output="$(dotnet list "$repo_root/src/worker/Worker.csproj" package --vulnerable --include-transitive)"
aspire_output="$(dotnet list "$repo_root/src/aspire/aspire.csproj" package --vulnerable --include-transitive)"

if ! grep -q "has no vulnerable packages" <<<"$backend_output"; then
  echo "[vuln-check] Backend vulnerabilities detected"
  echo "$backend_output"
  exit 1
fi

if ! grep -q "has no vulnerable packages" <<<"$worker_output"; then
  echo "[vuln-check] Worker vulnerabilities detected"
  echo "$worker_output"
  exit 1
fi

if ! grep -q "has no vulnerable packages" <<<"$aspire_output"; then
  echo "[vuln-check] Aspire project vulnerabilities detected"
  echo "$aspire_output"

  fail_on_aspire_vulns="${DOTNET_VULN_FAIL_ON_ASPIRE:-false}"
  if [[ "${fail_on_aspire_vulns,,}" == "true" ]]; then
    echo "[vuln-check] Failing because DOTNET_VULN_FAIL_ON_ASPIRE=true"
    exit 1
  fi

  echo "[vuln-check] Continuing because aspire is a dev-orchestration project; set DOTNET_VULN_FAIL_ON_ASPIRE=true to enforce blocking."
fi

echo "[vuln-check] .NET checks passed"

echo "[vuln-check] Checking npm production dependency vulnerabilities"
audit_json="$(cd "$repo_root/src/frontend" && npm audit --omit=dev --json || true)"

counts="$(node -e '
let s = "";
process.stdin.on("data", d => s += d);
process.stdin.on("end", () => {
  const data = JSON.parse(s);
  const v = (data.metadata && data.metadata.vulnerabilities) || {};
  const moderate = Number(v.moderate || 0);
  const high = Number(v.high || 0);
  const critical = Number(v.critical || 0);
  console.log(`${moderate} ${high} ${critical}`);
});
' <<<"$audit_json")"

read -r moderate high critical <<<"$counts"

max_moderate="${NPM_AUDIT_MAX_MODERATE:-0}"
max_high="${NPM_AUDIT_MAX_HIGH:-0}"
max_critical="${NPM_AUDIT_MAX_CRITICAL:-0}"

echo "[vuln-check] npm severity counts: moderate=$moderate high=$high critical=$critical"
echo "[vuln-check] npm allowed thresholds: moderate<=$max_moderate high<=$max_high critical<=$max_critical"

if (( critical > max_critical || high > max_high || moderate > max_moderate )); then
  echo "[vuln-check] npm vulnerability threshold exceeded"
  exit 1
fi

echo "[vuln-check] Dependency vulnerability checks passed"
