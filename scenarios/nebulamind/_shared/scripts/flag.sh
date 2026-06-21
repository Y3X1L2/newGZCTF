#!/bin/sh

flag_to_env_key() {
    key="$(printf '%s' "$1" | tr '[:lower:]' '[:upper:]' | tr -c 'A-Z0-9' '_')"
    while [ "${key#_}" != "$key" ]; do key="${key#_}"; done
    while [ "${key%_}" != "$key" ]; do key="${key%_}"; done
    printf '%s' "$key"
}

flag_normalize_name() {
    name="$1"
    case "$name" in
        FLAG_*) printf '%s' "${name#FLAG_}" ;;
        *) printf '%s' "$name" ;;
    esac
}

get_flag() {
    name="$(flag_normalize_name "$1")"
    default="${2:-flag{not_configured}}"

    key="$(flag_to_env_key "$name")"
    if [ -n "$key" ]; then
        eval "val=\${GZCTF_FLAG_${key}:-}"
        [ -n "$val" ] && { printf '%s\n' "$val"; return 0; }
    fi

    [ -n "${GZCTF_FLAG:-}" ] && { printf '%s\n' "$GZCTF_FLAG"; return 0; }
    printf '%s\n' "$default"
}

write_flag_file() {
    file="$1"
    name="$2"
    perm="${3:-0644}"
    val="$(get_flag "$name")"
    printf '%s\n' "$val" > "$file"
    chmod "$perm" "$file"
}
