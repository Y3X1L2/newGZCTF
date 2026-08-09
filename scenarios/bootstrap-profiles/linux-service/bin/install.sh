#!/usr/bin/env sh
set -eu

test -f /opt/gzctf/service/server.py
test -f /etc/systemd/system/gzctf-runtime.service
command -v python3 >/dev/null 2>&1

systemctl daemon-reload
systemctl enable --now gzctf-runtime.service
