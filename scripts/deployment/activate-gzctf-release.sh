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

current="$root/publish"
previous="$root/publish.previous"
release_root="$root/releases/$release_id"
next="$release_root/publish.partial"
release="$release_root/publish"
next_link="$root/publish.next-$release_id"
previous_link="$root/publish.previous.next-$release_id"
agent_path="/usr/local/bin/gzctf-agent"
agent_staging="$release_root/gzctf-agent.next"
agent_previous="$release_root/gzctf-agent.previous"
agent_is_symlink=false

test -L "$current" || { echo "$current must be a symbolic link" >&2; exit 4; }
old_release="$(readlink -f "$current")"
case "$old_release" in
  "$root"/releases/*/publish) ;;
  *) echo "current release is outside $root/releases" >&2; exit 4 ;;
esac
test -f "$old_release/GZCTF" || { echo "current release is incomplete" >&2; exit 4; }
test -f "$old_release/appsettings.json" || { echo "current configuration is missing" >&2; exit 4; }
files_target="${GZCTF_PERSISTENT_FILES:-/opt/gzctf/persistent/files}"
test -d "$files_target" || { echo "persistent file storage is missing at $files_target" >&2; exit 4; }
test ! -e "$release_root" || { echo "release directory already exists" >&2; exit 4; }

install -d -m 0750 "$release_root"
mkdir "$next"
tar -xzf "$archive_path" -C "$next"
test -f "$next/GZCTF" && test -f "$next/efbundle" && test -f "$next/release-manifest.json"
find "$next" -type d -exec chmod 0755 {} +
find "$next" -type f -exec chmod 0644 {} +
chmod 0755 \
  "$next/GZCTF" \
  "$next/efbundle" \
  "$next/agent/gzctf-agent" \
  "$next/agent/endpoint-sensor/linux-x64/gzctf-endpoint-sensor" \
  "$next/agent/guest-supervisor/linux-x64/gzctf-guest-supervisor" \
  "$next/agent/guest-supervisor/win-x64/gzctf-guest-supervisor.exe"

# The systemd unit uses a stable host path rather than the application release
# symlink. Stage the matching Agent before stopping services so the local
# execution plane cannot silently remain on an older binary.
if test -L "$agent_path"; then
  agent_is_symlink=true
  readlink "$agent_path" > "$agent_previous"
elif test -e "$agent_path"; then
  install -m 0755 "$agent_path" "$agent_previous"
fi
install -m 0755 "$next/agent/gzctf-agent" "$agent_staging"

rm -rf "$next/files"
ln -s "$files_target" "$next/files"
install -m 0600 "$old_release/appsettings.json" "$next/appsettings.json"
mv "$next" "$release"

# The caller must create and verify a database backup before activation. Stop
# both writers before applying schema changes against that rollback boundary.
systemctl stop gzctf-agent.service
systemctl stop gzctf.service
if ss -lntp | grep -q ':8080 '; then
  echo 'port 8080 is still listening after service stop' >&2
  systemctl start gzctf-agent.service || true
  systemctl start gzctf.service || true
  exit 5
fi

if ! database_connection="$(python3 -c 'import json, sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["ConnectionStrings"]["Database"])' "$release/appsettings.json")"; then
  echo "database connection could not be read" >&2
  systemctl start gzctf-agent.service || true
  systemctl start gzctf.service || true
  exit 5
fi
if test -z "$database_connection"; then
  echo "database connection is missing" >&2
  systemctl start gzctf-agent.service || true
  systemctl start gzctf.service || true
  exit 5
fi
if ! (cd "$release" && ./efbundle --no-color --connection "$database_connection"); then
  echo "migration failed; restoring previous release services" >&2
  systemctl start gzctf-agent.service || true
  systemctl start gzctf.service || true
  rm -f "$agent_staging" "$agent_previous"
  rm -rf "$release_root"
  exit 5
fi
unset database_connection

rm -f "$next_link" "$previous_link"
ln -s "$old_release" "$previous_link"
mv -Tf "$previous_link" "$previous"
ln -s "$release" "$next_link"
mv -Tf "$next_link" "$current"
if test "$agent_is_symlink" = true; then
  rm -f "$agent_path"
  rm -f "$agent_staging"
  ln -s "$release/agent/gzctf-agent" "$agent_staging"
fi
mv -Tf "$agent_staging" "$agent_path"
systemctl daemon-reload
systemctl start gzctf.service || true
systemctl start gzctf-agent.service || true

healthy=false
for _ in $(seq 1 45); do
  if systemctl is-active --quiet gzctf.service && \
     systemctl is-active --quiet gzctf-agent.service && \
     curl -fsS --max-time 3 http://127.0.0.1:8080/ >/dev/null; then
    healthy=true
    break
  fi
  sleep 1
done
if test "$healthy" != true; then
  failed="$release_root/publish.failed"
  systemctl stop gzctf.service || true
  systemctl stop gzctf-agent.service || true
  if test "$agent_is_symlink" = true; then
    rm -f "$agent_path"
    ln -s "$(cat "$agent_previous")" "$agent_staging"
    mv -Tf "$agent_staging" "$agent_path"
  elif test -f "$agent_previous"; then
    mv -Tf "$agent_previous" "$agent_path"
  else
    rm -f "$agent_path"
  fi
  mv "$release" "$failed"
  ln -sfn "$old_release" "$next_link"
  mv -Tf "$next_link" "$current"
  systemctl start gzctf.service
  systemctl start gzctf-agent.service
  echo "release activation failed; previous release link restored" >&2
  exit 6
fi

rm -f "$agent_previous"
rm -f "$archive_path"
echo "release=$release_id status=active"
