#!/bin/sh
set -eu

socat TCP-LISTEN:15000,bind=127.0.0.1,reuseaddr,fork TCP:registry:5000 &
exec dotnet GZCTF.dll
