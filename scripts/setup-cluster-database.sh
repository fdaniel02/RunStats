#!/usr/bin/env bash
# Sets up RunStats' database on the cluster's SQL Server from the local development database, and seals the app's and
# the gateway's secrets into a cluster-gitops checkout. Run once from WSL (kubectl, kubeseal, Docker Desktop).
# No password is printed or written to a file in plain text.
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: scripts/setup-cluster-database.sh [--replace] [--gitops DIR] [--secrets DIR] [--dev-container NAME]

  --replace          overwrite an existing RunStats database on the cluster
  --gitops DIR       cluster-gitops checkout (default /mnt/c/_WORK/cluster-gitops)
  --secrets DIR      folder with dev-sql-password.txt and gateway.env (default /mnt/c/Users/work/.runstats)
  --dev-container    the local SQL Server container (default runstats-dev-sql)
EOF
}

replace=0
gitops=/mnt/c/_WORK/cluster-gitops
secrets=/mnt/c/Users/work/.runstats
dev_container=runstats-dev-sql
while [[ $# -gt 0 ]]; do
    case "$1" in
        --replace) replace=1 ;;
        --gitops) gitops="$2"; shift ;;
        --secrets) secrets="$2"; shift ;;
        --dev-container) dev_container="$2"; shift ;;
        -h|--help) usage; exit 0 ;;
        *) usage >&2; exit 2 ;;
    esac
    shift
done

die() { echo "Error: $*" >&2; exit 1; }
step() { echo "==> $*"; }

for tool in kubectl kubeseal openssl; do
    command -v "$tool" >/dev/null || die "$tool is missing"
done
# Without Docker Desktop's WSL integration, docker in WSL is a stub; the Windows client works through interop.
docker=docker
"$docker" version >/dev/null 2>&1 || docker=docker.exe
"$docker" version >/dev/null 2>&1 || die "docker isn't reachable from WSL"
[[ -d "$gitops/workloads" ]] || die "$gitops isn't a cluster-gitops checkout"
[[ -r "$secrets/dev-sql-password.txt" && -r "$secrets/gateway.env" ]] || die "$secrets needs dev-sql-password.txt and gateway.env"

# sa's password stays in the pod: sqlcmd reads it from the server's own environment.
# shellcheck disable=SC2016 # $MSSQL_SA_PASSWORD is meant to expand in the pod, not here.
cluster_sql() {
    kubectl exec -i -n mssql mssql-0 -- bash -c \
        '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -h -1 -W -i /dev/stdin'
}

dev_sql() {
    "$docker" exec -i -e SQLCMDPASSWORD="$(tr -d '\r\n' < "$secrets/dev-sql-password.txt")" "$dev_container" \
        /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -h -1 -W -i /dev/stdin
}

existing=$(cluster_sql <<<"SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'RunStats';" | tr -d '[:space:]')
[[ "$existing" == 0 || "$existing" == 1 ]] || die "couldn't query the cluster's SQL Server: $existing"
if [[ "$existing" == 1 && $replace == 0 ]]; then
    die "the cluster already has a RunStats database; use --replace to overwrite it"
fi

transfer=$(mktemp -d)
trap 'rm -rf "$transfer"' EXIT

step "Backing up the local database ($dev_container)"
"$docker" exec "$dev_container" mkdir -p /var/opt/mssql/backup
dev_sql <<'SQL'
BACKUP DATABASE [RunStats] TO DISK = N'/var/opt/mssql/backup/RunStats-transfer.bak' WITH COPY_ONLY, INIT, FORMAT, CHECKSUM;
SQL
"$docker" exec "$dev_container" cat /var/opt/mssql/backup/RunStats-transfer.bak >"$transfer/RunStats.bak"
"$docker" exec "$dev_container" rm -f /var/opt/mssql/backup/RunStats-transfer.bak

step "Copying it into the cluster's SQL Server ($(du -h "$transfer/RunStats.bak" | cut -f1))"
kubectl exec -i -n mssql mssql-0 -- bash -c 'mkdir -p /var/opt/mssql/restore && cat >/var/opt/mssql/restore/RunStats-transfer.bak' \
    <"$transfer/RunStats.bak"

step "Restoring it as RunStats"
cluster_sql <<SQL
IF DB_ID(N'RunStats') IS NOT NULL
    ALTER DATABASE [RunStats] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [RunStats] FROM DISK = N'/var/opt/mssql/restore/RunStats-transfer.bak'
    WITH MOVE N'RunStats' TO N'/var/opt/mssql/data/RunStats.mdf',
         MOVE N'RunStats_log' TO N'/var/opt/mssql/data/RunStats_log.ldf',
         CHECKSUM, REPLACE, RECOVERY;
ALTER DATABASE [RunStats] SET MULTI_USER;
-- Everything can be synced from Garmin again, so there's no point-in-time restore to keep log backups for; under FULL
-- the log would only grow between the daily full backups.
ALTER DATABASE [RunStats] SET RECOVERY SIMPLE;
-- The backup's owner is the local server's sa, which this server doesn't know.
ALTER AUTHORIZATION ON DATABASE::[RunStats] TO [sa];
SQL
kubectl exec -n mssql mssql-0 -- rm -f /var/opt/mssql/restore/RunStats-transfer.bak

# A login needs three of four character kinds; base64 without its symbols gives letters and digits.
password="$(openssl rand -base64 48 | tr -d '/+=\n' | cut -c1-40)"
[[ ${#password} -ge 32 ]] || die "couldn't generate a password"

step "Creating the runstats login and the users"
cluster_sql <<SQL
USE [RunStats];
-- The development history and its Data Protection keys don't belong in production.
DELETE FROM DataProtectionKeys;
DELETE FROM SyncRuns;
USE [master];
IF SUSER_ID(N'runstats') IS NULL
    CREATE LOGIN [runstats] WITH PASSWORD = N'$password', DEFAULT_DATABASE = [RunStats], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
    ALTER LOGIN [runstats] WITH PASSWORD = N'$password';
USE [RunStats];
CREATE USER [runstats] FOR LOGIN [runstats];
ALTER ROLE [db_datareader] ADD MEMBER [runstats];
ALTER ROLE [db_datawriter] ADD MEMBER [runstats];
ALTER ROLE [db_ddladmin] ADD MEMBER [runstats];
-- The nightly backup job logs in as backup and fails on a database it can't back up.
CREATE USER [backup] FOR LOGIN [backup];
ALTER ROLE [db_backupoperator] ADD MEMBER [backup];
SQL

step "Sealing the secrets into $gitops/workloads/runstats"
mkdir -p "$gitops/workloads/runstats"
# From env files on file descriptors, so the values never appear in a process's arguments.
kubectl create secret generic runstats-db --namespace runstats --dry-run=client -o yaml \
    --from-env-file=<(printf 'ConnectionStrings__Default=Server=mssql.mssql.svc.cluster.local;Database=RunStats;User Id=runstats;Password=%s;TrustServerCertificate=True\n' "$password") \
    | kubeseal -o yaml >"$gitops/workloads/runstats/sealedsecret-db.yaml"
kubectl create secret generic garmin-gateway --namespace runstats --dry-run=client -o yaml \
    --from-env-file=<(tr -d '\r' <"$secrets/gateway.env") \
    | kubeseal -o yaml >"$gitops/workloads/runstats/sealedsecret-garmin.yaml"
unset password

step "Done: $(cluster_sql <<<"SET NOCOUNT ON; SELECT COUNT(*) FROM RunStats.dbo.Runs;" | tr -d '[:space:]') runs in RunStats"
