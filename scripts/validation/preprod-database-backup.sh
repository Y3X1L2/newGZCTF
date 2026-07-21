#!/usr/bin/env bash
set -euo pipefail

container="${GZCTF_POSTGRES_CONTAINER:-gzctf-postgres}"
database="${GZCTF_DATABASE:-gzctf}"
backup_root="${GZCTF_VALIDATION_ROOT:-/opt/gzctf-vnext}/backups"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup_path="${backup_root}/${database}-before-vnext-${timestamp}.dump"
started_at="$(date +%s)"

sudo install -d -m 750 -o "$(id -un)" -g "$(id -gn)" "$backup_root"
sudo docker exec "$container" pg_dump -U postgres -d "$database" -Fc > "$backup_path"

finished_at="$(date +%s)"
sha256sum "$backup_path" | tee "${backup_path}.sha256"
ln -sfn "$backup_path" "${backup_root}/latest.dump"
ln -sfn "${backup_path}.sha256" "${backup_root}/latest.dump.sha256"

printf 'backup=%s\n' "$backup_path"
printf 'size_bytes=%s\n' "$(stat -c %s "$backup_path")"
printf 'duration_seconds=%s\n' "$((finished_at - started_at))"
printf 'database_size=%s\n' "$(sudo docker exec "$container" psql -U postgres -d "$database" -Atc \
  'select pg_size_pretty(pg_database_size(current_database()));')"
printf 'migration_head=%s\n' "$(sudo docker exec "$container" psql -U postgres -d "$database" -Atc \
  'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId" desc limit 1;')"
