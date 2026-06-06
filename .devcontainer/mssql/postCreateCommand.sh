#!/bin/bash
set -euo pipefail

SApassword=$1
dacpath=$2
sqlpath=$3
SQLSERVER_HOST=${SQLSERVER_HOST:-db}
SQLSERVER_PORT=${SQLSERVER_PORT:-1433}
SQLSERVER="${SQLSERVER_HOST},${SQLSERVER_PORT}"

SQLCMD=""
if command -v sqlcmd >/dev/null 2>&1; then
    SQLCMD=$(command -v sqlcmd)
elif [ -x /opt/mssql-tools18/bin/sqlcmd ]; then
    SQLCMD=/opt/mssql-tools18/bin/sqlcmd
elif [ -x /opt/mssql-tools/bin/sqlcmd ]; then
    SQLCMD=/opt/mssql-tools/bin/sqlcmd
fi

if [ -z "$SQLCMD" ]; then
    echo "sqlcmd is not available. Skipping local SQL initialization."
    exit 0
fi

cat <<'EOF' > testsqlconnection.sql
SELECT name FROM sys.databases;
EOF

for i in {1..60}; do
    if "$SQLCMD" -S "$SQLSERVER" -U sa -P "$SApassword" -d master -i testsqlconnection.sql >/dev/null 2>&1; then
        echo "SQL server ready at ${SQLSERVER}."
        break
    fi

    if [ "$i" -eq 60 ]; then
        echo "SQL server did not become ready in time."
        rm -f testsqlconnection.sql
        exit 1
    fi

    echo "Waiting for SQL server at ${SQLSERVER}..."
    sleep 1
done

rm -f testsqlconnection.sql

shopt -s nullglob
sql_files=("$sqlpath"/*.sql)
dacpac_files=("$dacpath"/*.dacpac)

for f in "${sql_files[@]}"; do
    echo "Executing $f"
    "$SQLCMD" -S "$SQLSERVER" -U sa -P "$SApassword" -d master -i "$f"
done

for f in "${dacpac_files[@]}"; do
    dbname=$(basename "$f" ".dacpac")
    echo "Deploying dacpac $f"
    /opt/sqlpackage/sqlpackage /Action:Publish /SourceFile:"$f" /TargetServerName:"$SQLSERVER_HOST" /TargetDatabaseName:"$dbname" /TargetUser:sa /TargetPassword:"$SApassword"
done
