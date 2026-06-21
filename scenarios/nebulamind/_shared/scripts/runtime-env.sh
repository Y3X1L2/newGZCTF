#!/bin/sh
set -eu

# NebulaMind platform runtime helpers.
# Images in this scenario must receive concrete per-team internal addresses from
# the GZCTF penetration orchestrator. Docker service-name fallbacks are
# intentionally not provided: missing cross-service configuration should fail
# fast instead of producing a topology that looks healthy but breaks in play.

nm_die() {
    echo "[nebulamind-runtime] ERROR: $*" >&2
    exit 1
}

nm_require() {
    var_name="$1"
    eval "var_value=\${$var_name:-}"
    [ -n "$var_value" ] || nm_die "required environment variable $var_name is not set"
}

nm_require_all() {
    for var_name in "$@"; do
        nm_require "$var_name"
    done
}

nm_sed_escape() {
    printf '%s' "$1" | sed -e 's/[\\&|]/\\&/g'
}

nm_replace_file() {
    file_path="$1"
    placeholder="$2"
    value="$3"
    [ -f "$file_path" ] || nm_die "cannot render missing file $file_path"
    escaped_value="$(nm_sed_escape "$value")"
    sed -i "s|$placeholder|$escaped_value|g" "$file_path"
}

nm_render_required_placeholders() {
    file_path="$1"
    shift
    for pair in "$@"; do
        placeholder="${pair%%=*}"
        var_name="${pair#*=}"
        nm_require "$var_name"
        eval "value=\${$var_name}"
        nm_replace_file "$file_path" "$placeholder" "$value"
    done
}
