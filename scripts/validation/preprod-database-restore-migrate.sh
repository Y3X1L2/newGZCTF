#!/usr/bin/env bash
set -euo pipefail

container="${GZCTF_POSTGRES_CONTAINER:-gzctf-postgres}"
source_database="${GZCTF_DATABASE:-gzctf}"
validation_root="${GZCTF_VALIDATION_ROOT:-/opt/gzctf-vnext}"
backup_path="${GZCTF_BACKUP_PATH:-${validation_root}/backups/latest.dump}"
migration_bundle="${GZCTF_MIGRATION_BUNDLE:-${validation_root}/preprod/gzctf-migrate}"
preprod_database="${GZCTF_PREPROD_DATABASE:-gzctf_vnext_preprod}"
restore_database="${GZCTF_RESTORE_DATABASE:-gzctf_restore_verify}"
report_root="${validation_root}/reports/$(date -u +%Y%m%dT%H%M%SZ)"

case "$preprod_database:$restore_database" in
  gzctf_vnext_*:gzctf_restore_*) ;;
  *) printf 'Refusing unsafe validation database names.\n' >&2; exit 2 ;;
esac

sudo install -d -m 750 -o "$(id -un)" -g "$(id -gn)" "$report_root"
test -s "$backup_path"
test -x "$migration_bundle"
sha256sum -c "${backup_path}.sha256"
sudo docker exec -i "$container" pg_restore --list < "$backup_path" > "${report_root}/backup-contents.txt"

psql_value() {
  local database="$1"
  local sql="$2"
  sudo docker exec "$container" psql -U postgres -d "$database" -Atc "$sql"
}

restore_database_from_backup() {
  local database="$1"
  sudo docker exec "$container" dropdb -U postgres --if-exists --force "$database"
  sudo docker exec "$container" createdb -U postgres "$database"
  sudo docker exec -i "$container" pg_restore -U postgres -d "$database" \
    --exit-on-error --no-owner --no-privileges < "$backup_path"
}

snapshot_counts() {
  local database="$1"
  local output="$2"
  while IFS= read -r table; do
    printf '%s\t%s\n' "$table" "$(psql_value "$database" "select count(*) from \"$table\";")"
  done < <(psql_value "$database" \
    "select tablename from pg_tables where schemaname='public' order by tablename;") \
    > "$output"
}

snapshot_core_counts() {
  local database="$1"
  local output="$2"
  local tables=(
    AspNetUsers Teams TeamUserInfo Games GameChallenges GameInstances Submissions
    TheoryAnswerSheets TheoryExamSubmissions TheorySubmissionAnswers
    TrainingCourses TrainingCourseChapters TrainingCourseEnrollments
    TrainingCourseProgresses TrainingCourseSubmissions
    TrainingCourseChapterTheorySheets TrainingCourseChapterTheoryAnswers
    AwdpServices AwdpRounds AwdpFlags AwdpPatchSubmissions AwdpServiceInstances
    AwdpResetRecords AwdpRecoveryRecords ImageTemplates ImageDistributionRecords
    ImageImportJobs WorkerNodes
  )

  : > "$output"
  for table in "${tables[@]}"; do
    if [[ "$(psql_value "$database" "select to_regclass('public.\"$table\"') is not null;")" == "t" ]]; then
      printf '%s\t%s\n' "$table" "$(psql_value "$database" "select count(*) from \"$table\";")" >> "$output"
    else
      printf '%s\tMISSING\n' "$table" >> "$output"
    fi
  done
}

printf 'Creating isolated restore databases...\n'
restore_started="$(date +%s)"
restore_database_from_backup "$restore_database"
restore_database_from_backup "$preprod_database"
restore_finished="$(date +%s)"

snapshot_counts "$source_database" "${report_root}/source-current-all.tsv"
snapshot_counts "$restore_database" "${report_root}/restore-all.tsv"
snapshot_counts "$preprod_database" "${report_root}/preprod-before-all.tsv"
snapshot_core_counts "$source_database" "${report_root}/source-current-core.tsv"
snapshot_core_counts "$restore_database" "${report_root}/restore-core.tsv"
snapshot_core_counts "$preprod_database" "${report_root}/preprod-before-core.tsv"

diff -u "${report_root}/restore-all.tsv" "${report_root}/preprod-before-all.tsv" \
  > "${report_root}/restore-repeatability.diff" || true

postgres_password="$(sudo docker inspect "$container" --format '{{range .Config.Env}}{{println .}}{{end}}' \
  | sed -n 's/^POSTGRES_PASSWORD=//p' | head -n 1)"
test -n "$postgres_password"

printf 'Applying migration bundle to isolated pre-production database...\n'
migration_started="$(date +%s)"
"$migration_bundle" --connection \
  "Host=127.0.0.1;Port=5432;Database=${preprod_database};Username=postgres;Password=${postgres_password};Include Error Detail=true" \
  2>&1 | tee "${report_root}/migration.log"
migration_finished="$(date +%s)"
unset postgres_password

snapshot_counts "$preprod_database" "${report_root}/preprod-after-all.tsv"
snapshot_core_counts "$preprod_database" "${report_root}/preprod-after-core.tsv"
diff -u "${report_root}/preprod-before-core.tsv" "${report_root}/preprod-after-core.tsv" \
  > "${report_root}/core-counts.diff" || true

psql_value "$preprod_database" \
  'select "MigrationId" from "__EFMigrationsHistory" order by "MigrationId";' \
  > "${report_root}/migrations-after.txt"

printf 'report_root=%s\n' "$report_root"
printf 'restore_duration_seconds=%s\n' "$((restore_finished - restore_started))"
printf 'migration_duration_seconds=%s\n' "$((migration_finished - migration_started))"
printf 'restore_repeatability_diff_lines=%s\n' \
  "$(wc -l < "${report_root}/restore-repeatability.diff")"
printf 'core_count_diff_lines=%s\n' "$(wc -l < "${report_root}/core-counts.diff")"
printf 'migration_head=%s\n' "$(tail -n 1 "${report_root}/migrations-after.txt")"
