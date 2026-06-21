#!/bin/sh
set -e

. /_shared/scripts/flag.sh
. /_shared/scripts/runtime-env.sh

nm_require_all NM_CACHE_BROKER_HOST

FLAG_D1="$(get_flag 'FLAG_REDIS_QUEUE_INFO' 'flag{d1_redis_queue_info_placeholder}')"

echo "[cache-broker] D1 flag available via env (FLAG_REDIS_QUEUE_INFO)"
echo "[cache-broker] seeding Redis with task queue data..."

redis-server /usr/local/etc/redis/redis.conf --daemonize yes --bind 127.0.0.1 --port 6379

ready=0
for _ in $(seq 1 30); do
    if redis-cli -h 127.0.0.1 -p 6379 ping 2>/dev/null | grep -q PONG; then
        ready=1
        break
    fi
    sleep 0.5
done

if [ "$ready" -ne 1 ]; then
    echo "[cache-broker] ERROR: temporary Redis did not start within 15s" >&2
    exit 1
fi

redis-cli --eval /usr/local/etc/redis/init.lua , "$FLAG_D1" "$NM_CACHE_BROKER_HOST"
redis-cli -h 127.0.0.1 -p 6379 save
redis-cli -h 127.0.0.1 -p 6379 shutdown nosave 2>/dev/null || true

sleep 1

echo "[cache-broker] seed data injected, starting Redis in foreground..."
echo "[cache-broker] listening on 0.0.0.0:6379 (no auth - vulnerability by design)"

exec "$@"
