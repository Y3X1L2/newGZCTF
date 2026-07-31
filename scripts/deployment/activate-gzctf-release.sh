#!/usr/bin/env bash
set -euo pipefail

release_id="${1:?release id is required}"
archive_path="${2:?archive path is required}"
expected_sha="${3:?archive sha256 is required}"
root="${GZCTF_DEPLOY_ROOT:-/opt/gzctf}"

case "$release_id" in
  *[!A-Za-z0-9._-]*|'') echo "invalid release id" >&2; exit 2 ;;
esac
case "$archive_path" in
  "$root"/incoming/*) ;;
  *) echo "archive must be under $root/incoming" >&2; exit 2 ;;
esac

actual_sha="$(sha256sum "$archive_path" | awk '{print $1}')"
test "$actual_sha" = "$expected_sha" || { echo "archive sha256 mismatch" >&2; exit 3; }

next="$root/publish.next-$release_id"
current="$root/publish"
previous="$root/publish.previous"
persistent="$root/persistent/files"
config="$root/persistent/appsettings.json"
failed="$root/publish.failed-$release_id"
test ! -e "$next" || { echo "next release directory already exists" >&2; exit 4; }
if test -e "$failed"; then rm -rf "$failed"; fi
mkdir -p "$next" "$root/persistent"
tar -xzf "$archive_path" -C "$next"
test -f "$next/GZCTF" && test -f "$next/release-manifest.json"
find "$next" -type d -exec chmod 0755 {} +
find "$next" -type f -exec chmod 0644 {} +
chmod 0755 \
  "$next/GZCTF" \
  "$next/efbundle" \
  "$next/agent/gzctf-agent" \
  "$next/agent/endpoint-sensor/linux-x64/gzctf-endpoint-sensor" \
  "$next/agent/guest-supervisor/linux-x64/gzctf-guest-supervisor" \
  "$next/agent/guest-supervisor/win-x64/gzctf-guest-supervisor.exe"

if test ! -e "$persistent"; then
  test -d "$current/files" || { echo "current persistent files directory is missing" >&2; exit 5; }
  mv "$current/files" "$persistent"
fi
rm -rf "$next/files"
ln -s "$persistent" "$next/files"
if test -d "$current" && test ! -L "$current/files"; then
  rm -rf "$current/files"
  ln -s "$persistent" "$current/files"
fi
if test ! -e "$config"; then
  test -f "$current/appsettings.json" || { echo "current appsettings.json is missing" >&2; exit 5; }
  mv "$current/appsettings.json" "$config"
  chmod 0640 "$config"
fi
rm -f "$next/appsettings.json"
ln -s "$config" "$next/appsettings.json"
if test -d "$current" && test ! -L "$current/appsettings.json"; then
  rm -f "$current/appsettings.json"
  ln -s "$config" "$current/appsettings.json"
fi

# Apply schema changes against the same persistent configuration that the new
# service will use. EF migration bundles do not load the platform configuration
# by themselves, so pass the configured database connection explicitly.
database_connection="$(python3 -c 'import json, sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["ConnectionStrings"]["Database"])' "$config")"
test -n "$database_connection" || { echo "database connection is missing" >&2; exit 5; }
(cd "$next" && ./efbundle --no-color --connection "$database_connection")

systemctl stop gzctf.service
if test -e "$previous"; then rm -rf "$previous"; fi
if test -e "$current"; then mv "$current" "$previous"; fi
mv "$next" "$current"
chown -h "$(stat -c '%U:%G' "$persistent")" "$current/files"
systemctl start gzctf.service

healthy=false
for _ in $(seq 1 45); do
  if systemctl is-active --quiet gzctf.service && curl -fsS --max-time 3 http://127.0.0.1:8080/ >/dev/null; then
    healthy=true
    break
  fi
  sleep 1
done
if test "$healthy" != true; then
  systemctl stop gzctf.service || true
  mv "$current" "$failed"
  mv "$previous" "$current"
  systemctl start gzctf.service
  echo "release activation failed; previous release restored" >&2
  exit 6
fi

rm -f "$archive_path"
echo "release=$release_id status=active"
