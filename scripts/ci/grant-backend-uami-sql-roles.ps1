$ErrorActionPreference = 'Stop'

$mode = "${env:SQL_PROVISIONING_MODE}".ToLowerInvariant()
if ($mode -eq 'existing') {
  Write-Host 'SQL_PROVISIONING_MODE=existing; skipping automatic SQL role grants for UAMI.'
  exit 0
}

$sqlServer = azd env get-value AZURE_SQL_SERVER_NAME
$sqlDatabase = azd env get-value AZURE_SQL_DATABASE_NAME
$uamiName = azd env get-value UAMI_NAME

if ([string]::IsNullOrWhiteSpace($sqlServer) -or [string]::IsNullOrWhiteSpace($sqlDatabase) -or [string]::IsNullOrWhiteSpace($uamiName)) {
  throw 'Missing required azd outputs for SQL role grant. Expected AZURE_SQL_SERVER_NAME, AZURE_SQL_DATABASE_NAME, and UAMI_NAME.'
}

$token = az account get-access-token --resource https://database.windows.net/ --query accessToken -o tsv
if ([string]::IsNullOrWhiteSpace($token)) {
  throw 'Failed to acquire Azure SQL access token from current OIDC login.'
}

$connectionString = "Server=tcp:$sqlServer.database.windows.net,1433;Initial Catalog=$sqlDatabase;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

$escapedUamiName = $uamiName.Replace("'", "''")

$sql = @"
  DECLARE @principal sysname = N'$escapedUamiName';

  IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @principal)
  BEGIN
    DECLARE @createUserSql nvarchar(max) = N'CREATE USER [' + REPLACE(@principal, ']', ']]') + N'] FROM EXTERNAL PROVIDER;';
    EXEC(@createUserSql);
  END;

  IF NOT EXISTS (
    SELECT 1
    FROM sys.database_role_members drm
    JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id
    JOIN sys.database_principals m ON m.principal_id = drm.member_principal_id
    WHERE r.name = N'db_datareader' AND m.name = @principal
  )
  BEGIN
    DECLARE @addReaderSql nvarchar(max) = N'ALTER ROLE [db_datareader] ADD MEMBER [' + REPLACE(@principal, ']', ']]') + N'];';
    EXEC(@addReaderSql);
  END;

  IF NOT EXISTS (
    SELECT 1
    FROM sys.database_role_members drm
    JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id
    JOIN sys.database_principals m ON m.principal_id = drm.member_principal_id
    WHERE r.name = N'db_datawriter' AND m.name = @principal
  )
  BEGIN
    DECLARE @addWriterSql nvarchar(max) = N'ALTER ROLE [db_datawriter] ADD MEMBER [' + REPLACE(@principal, ']', ']]') + N'];';
    EXEC(@addWriterSql);
  END;

  IF NOT EXISTS (
    SELECT 1
    FROM sys.database_role_members drm
    JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id
    JOIN sys.database_principals m ON m.principal_id = drm.member_principal_id
    WHERE r.name = N'db_ddladmin' AND m.name = @principal
  )
  BEGIN
    DECLARE @addDdlSql nvarchar(max) = N'ALTER ROLE [db_ddladmin] ADD MEMBER [' + REPLACE(@principal, ']', ']]') + N'];';
    EXEC(@addDdlSql);
  END;
"@

Add-Type -AssemblyName System.Data
$conn = New-Object System.Data.SqlClient.SqlConnection $connectionString
$conn.AccessToken = $token
$conn.Open()

try {
  $cmd = $conn.CreateCommand()
  $cmd.CommandText = $sql
  [void]$cmd.ExecuteNonQuery()
  Write-Host "Granted SQL roles to UAMI '$uamiName' on database '$sqlDatabase'."
}
finally {
  $conn.Close()
}
