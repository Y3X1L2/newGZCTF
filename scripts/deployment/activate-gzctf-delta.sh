#!/usr/bin/env bash
set -euo pipefail

# Incremental activation: hard-link the previous release as the base, then
# overwrite only the changed files carried in the delta archive.
# Usage: activate-gzctf-delta.sh <release_id> <delta_archive> <expected_sha>
# Requires: previous release /opt/gzctf/publish (symlink) with release-manifest.json
release_id="${1:?release id is required}"
archive_path="${2:?delta archive path is required}"
expected_sha="${3:?delta archive sha256 is required}"
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

test -L "$current" || { echo "$current must be a symbolic link" >&2; exit 4; }
old_release="$(readlink -f "$current")"
case "$old_release" in
  "$root"/releases/*/publish) ;;
  *) echo "current release is outside $root/releases" >&2; exit 4 ;;
esac
test -f "$old_release/GZCTF" || { echo "current release is incomplete" >&2; exit 4; }
test -f "$old_release/appsettings.json" || { echo "current configuration is missing" >&2; exit 4; }
test -f "$old_release/release-manifest.json" || { echo "current release manifest is missing (full deploy required)" >&2; exit 4; }
test ! -e "$release_root" || { echo "release directory already exists" >&2; exit 4; }

# Validate the delta manifest before touching anything.
test -f "$archive_path" || { echo "delta archive missing" >&2; exit 2; }
tar -xzf "$archive_path" -O delta-manifest.json > /tmp/gzctf-delta-manifest.$$ 2>/dev/null \
  || { echo "delta archive has no delta-manifest.json" >&2; exit 4; }
python3 - "$old_release" /tmp/gzctf-delta-manifest.$$ <<'PY'
import json, os, sys
old_release, manifest_path = sys.argv[1], sys.argv[2]
with open(manifest_path, encoding="utf-8") as f:
    manifest = json.load(f)
assert manifest.get("schemaVersion") == 1, "delta manifest schema mismatch"
required = {"releaseId", "baseReleaseId", "files", "removed"}
missing = required - manifest.keys()
assert not missing, f"delta manifest missing keys: {missing}"
with open(os.path.join(old_release, "release-manifest.json"), encoding="utf-8") as f:
    base = json.load(f)
assert base.get("releaseId") == manifest.get("baseReleaseId"), \
    f"base release mismatch: server={base.get('releaseId')} delta expects={manifest.get('baseReleaseId')}"
for entry in manifest["files"]:
    assert {"path", "sha256"} <= entry.keys(), f"delta entry missing keys: {entry}"
print(f"delta validation ok: {len(manifest['files'])} files, {len(manifest.get('removed', []))} removed")
PY

# Hard-link the previous release as the base (instant, no data copy).
install -d -m 0750 "$release_root"
cp -al "$old_release" "$next"
rm -f "$next/files"
files_target="${GZCTF_PERSISTENT_FILES:-/opt/gzctf/persistent/files}"
test -d "$files_target" || { echo "persistent file storage is missing at $files_target" >&2; exit 4; }
ln -s "$files_target" "$next/files"

# Apply the delta: extract changed files over the base, delete removed ones.
# The base uses hard links for instant construction.  Remove the destination
# links before extraction so tar creates new inodes and cannot mutate the
# currently active release through a shared inode.
python3 - "$next" /tmp/gzctf-delta-manifest.$$ <<'PY'
import json, os, sys
release, manifest_path = sys.argv[1], sys.argv[2]
with open(manifest_path, encoding="utf-8") as f:
    manifest = json.load(f)
root = os.path.realpath(release)
for entry in manifest["files"]:
    relative = entry["path"]
    target = os.path.realpath(os.path.join(root, relative))
    if os.path.commonpath([root, target]) != root:
        raise RuntimeError(f"invalid delta path: {relative}")
    if os.path.isfile(target) or os.path.islink(target):
        os.unlink(target)
PY
tar -xzf "$archive_path" -C "$next"
python3 - "$next" /tmp/gzctf-delta-manifest.$$ <<'PY'
import json, os, sys
release, manifest_path = sys.argv[1], sys.argv[2]
with open(manifest_path, encoding="utf-8") as f:
    manifest = json.load(f)
for relative in manifest.get("removed", []):
    target = os.path.join(release, relative)
    if os.path.isfile(target):
        os.remove(target)
    elif os.path.islink(target):
        os.remove(target)
print("delta applied")
PY
rm -f /tmp/gzctf-delta-manifest.$$

# Re-verify the resulting release against the delta manifest.
python3 - "$next" "$archive_path" <<'PY'
import hashlib, json, os, sys, tempfile, tarfile
release, archive_path = sys.argv[1], sys.argv[2]
with tarfile.open(archive_path, "r:gz") as tar:
    with tempfile.NamedTemporaryFile(delete=False) as f:
        f.write(tar.extractfile("delta-manifest.json").read())
        manifest_path = f.name
with open(manifest_path, encoding="utf-8") as f:
    manifest = json.load(f)
failures = []
for entry in manifest["files"]:
    path = os.path.join(release, entry["path"])
    if not os.path.isfile(path):
        failures.append(f"{entry['path']}: missing")
        continue
    digest = hashlib.sha256(open(path, "rb").read()).hexdigest()
    if digest != entry["sha256"]:
        failures.append(f"{entry['path']}: sha256 mismatch")
os.unlink(manifest_path)
if failures:
    print("\n".join(failures[:20]))
    sys.exit(1)
print(f"delta verify ok: {len(manifest['files'])} files verified")
PY

# Preserve production configuration and permissions.
install -m 0600 "$old_release/appsettings.json" "$next/appsettings.json"
chmod 0755 "$next/GZCTF" "$next/efbundle"
mv "$next" "$release"

# The caller must create and verify a database backup before activation. Stop
# both writers before applying schema changes against that rollback boundary.
systemctl stop gzctf-agent.service
systemctl stop gzctf.service
if ss -lntp | grep -q ':8080 '; then
  echo 'port 8080 is still listening after service stop' >&2
  exit 5
fi

database_connection="$(python3 -c 'import json, sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["ConnectionStrings"]["Database"])' "$release/appsettings.json")"
test -n "$database_connection" || { echo "database connection is missing" >&2; exit 5; }
if ! (cd "$release" && ./efbundle --no-color --connection "$database_connection"); then
  echo "migration failed; restoring previous release services" >&2
  systemctl start gzctf-agent.service || true
  systemctl start gzctf.service || true
  rm -rf "$release_root"
  exit 5
fi
unset database_connection

rm -f "$next_link" "$previous_link"
ln -s "$old_release" "$previous_link"
mv -Tf "$previous_link" "$previous"
ln -s "$release" "$next_link"
mv -Tf "$next_link" "$current"
systemctl daemon-reload
systemctl start gzctf.service
systemctl start gzctf-agent.service

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
  mv "$release" "$failed"
  ln -sfn "$old_release" "$next_link"
  mv -Tf "$next_link" "$current"
  systemctl start gzctf.service
  systemctl start gzctf-agent.service
  echo "release activation failed; previous release link restored" >&2
  exit 6
fi

rm -f "$archive_path"
echo "release=$release_id status=active (delta from $old_release)"
