#!/bin/sh
set -eu
mkdir -p /tmp/guest/bin /tmp/guest/dev /tmp/guest/proc /tmp/guest/sys /tmp/guest/lib
cp /bin/busybox /tmp/guest/bin/
cp /opt/hybrid/guest-init /tmp/guest/init
chmod 755 /tmp/guest/init
module=$(find /lib/modules -name 'e1000.ko*' -print -quit)
test -n "$module"
case "$module" in
    *.zst) zstd -dc "$module" > /tmp/guest/e1000.ko ;;
    *.xz) xz -dc "$module" > /tmp/guest/e1000.ko ;;
    *) cp "$module" /tmp/guest/e1000.ko ;;
esac
cd /tmp/guest
find . -print0 | cpio --null -o --format=newc | gzip -1 > /tmp/guest.cpio.gz
