# Ensures a local SQL Server connection profile exists in .vscode/settings.json for the mssql extension.
# Idempotent: safe to run on every folder open.

$ErrorActionPreference = "Stop"

function Write-Info {
    param([string]$Message)
    Write-Host "[info] $Message"
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[warn] $Message"
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$aspireEnvPath = Join-Path $repoRoot "src\aspire\.env"
$settingsDir = Join-Path $env:APPDATA "Code\User"
$settingsPath = Join-Path $settingsDir "settings.json"

if (-not (Test-Path $settingsDir)) {
    New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
}

if (-not (Test-Path $settingsPath)) {
    Set-Content -Path $settingsPath -Value "{}`n" -NoNewline
}

if (Get-Command code -ErrorAction SilentlyContinue) {
    $installedExtensions = & code --list-extensions 2>$null
    if (-not ($installedExtensions -contains "ms-mssql.mssql")) {
        Write-Info "ms-mssql.mssql extension is not installed yet; skipping SQL profile bootstrap"
        exit 0
    }
}
else {
    Write-Info "VS Code CLI not found on PATH; continuing SQL profile bootstrap"
}

$sqlHostPort = "1433"

if (Test-Path $aspireEnvPath) {
    $portLine = Get-Content $aspireEnvPath | Where-Object { $_ -match '^\s*SQL_HOST_PORT\s*=' } | Select-Object -First 1
    if ($portLine -and $portLine -match '^\s*SQL_HOST_PORT\s*=\s*(\d+)\s*$') {
        $sqlHostPort = $Matches[1]
    }
}

try {
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        $dockerRows = & docker ps --filter "name=sql" --format "{{.Names}}|{{.Ports}}" 2>$null
        foreach ($row in $dockerRows) {
            if ($row -notmatch "^([^|]+)\|(.*)$") { continue }

            $name = $Matches[1]
            $ports = $Matches[2]
            if ($name -notlike "sql-*") { continue }

            if ($ports -match "127\.0\.0\.1:(\d+)->1433/tcp") {
                $sqlHostPort = $Matches[1]
                break
            }

            if ($ports -match ":(\d+)->1433/tcp") {
                $sqlHostPort = $Matches[1]
                break
            }
        }
    }
}
catch {
    Write-Info "Could not inspect running SQL container port mapping; using SQL_HOST_PORT=$sqlHostPort"
}

$server = "127.0.0.1"
$database = "AcaAspireAiTemplate"
$user = "sa"
$password = "P@ssw0rd"

$connectionProperty = @"
"mssql.connections": [
  {
        "id": "mssql-container-localhost-$sqlHostPort",
        "groupId": "ROOT",
    "server": "$server",
        "port": $sqlHostPort,
    "database": "$database",
    "authenticationType": "SqlLogin",
    "user": "$user",
    "password": "$password",
    "connectionString": "Server=$server,$sqlHostPort;Database=$database;User ID=$user;Password=$password;Encrypt=False;TrustServerCertificate=True;Connection Timeout=15",
        "encrypt": "Optional",
        "trustServerCertificate": true,
    "emptyPasswordInput": false,
    "savePassword": false,
    "profileName": "mssql-container"
  }
]
"@

$settingsRaw = [System.IO.File]::ReadAllText($settingsPath)
if ([string]::IsNullOrWhiteSpace($settingsRaw)) {
    $settingsRaw = "{}"
}

try {
    $settings = $settingsRaw | ConvertFrom-Json
}
catch {
    Write-Warn "VS Code user settings are not valid JSON. Skipping SQL profile bootstrap to avoid corrupting settings."
    exit 0
}

$connectionProfile = [pscustomobject]@{
    id = "mssql-container-localhost-$sqlHostPort"
    groupId = "ROOT"
    server = $server
    port = [int]$sqlHostPort
    database = $database
    authenticationType = "SqlLogin"
    user = $user
    password = $password
    connectionString = "Server=$server,$sqlHostPort;Database=$database;User ID=$user;Password=$password;Encrypt=False;TrustServerCertificate=True;Connection Timeout=15"
    encrypt = "Optional"
    trustServerCertificate = $true
    emptyPasswordInput = $false
    savePassword = $false
    profileName = "mssql-container"
}

if ($null -eq $settings.PSObject.Properties["mssql.connections"]) {
    $settings | Add-Member -NotePropertyName "mssql.connections" -NotePropertyValue @($connectionProfile)
}
else {
    $settings."mssql.connections" = @($connectionProfile)
}

$updated = $settings | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText($settingsPath, $updated)

Write-Info "SQL connection profile ensured in VS Code user settings (server=$server, profile=mssql-container)"
exit 0
