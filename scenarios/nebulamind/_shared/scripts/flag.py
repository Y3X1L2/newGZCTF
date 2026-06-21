#!/usr/bin/env python3
from __future__ import annotations

import os
import re


def to_env_key(value: str) -> str:
    value = value.strip().upper()
    if value.startswith("FLAG_"):
        value = value[5:]
    return re.sub(r"[^A-Z0-9]+", "_", value).strip("_")


def get_flag(name: str, default: str = "flag{not_configured}") -> str:
    key = to_env_key(name)
    if key:
        env_name = f"GZCTF_FLAG_{key}"
        if os.environ.get(env_name):
            return os.environ[env_name]
    if os.environ.get("GZCTF_FLAG"):
        return os.environ["GZCTF_FLAG"]
    return default


def write_flag_file(path: str, name: str, perm: int = 0o644, default: str = "flag{not_configured}") -> str:
    val = get_flag(name, default)
    with open(path, "w", encoding="utf-8") as f:
        f.write(val + "\n")
    os.chmod(path, perm)
    return val
