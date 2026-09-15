#!/bin/sh
set -eu

rm -f /var/run/docker.sock /var/run/docker.pid
dockerd --host=unix:///var/run/docker.sock --storage-driver=vfs --bridge=none \
  --iptables=false --insecure-registry=127.0.0.1:15000 >/tmp/dockerd.log 2>&1 &
for attempt in $(seq 1 60); do
  docker info >/dev/null 2>&1 && break
  if [ "$attempt" -eq 60 ]; then
    cat /tmp/dockerd.log
    exit 1
  fi
  sleep 1
done
sysctl -w net.ipv4.conf.all.route_localnet=1 >/dev/null
sysctl -w net.ipv4.conf.lo.route_localnet=1 >/dev/null
iptables -t nat -A OUTPUT -d 127.0.0.1/32 -p tcp --dport 15000 \
  -j DNAT --to-destination 10.250.0.12:5000
iptables -t nat -A POSTROUTING -d 10.250.0.12/32 -p tcp --dport 5000 -j MASQUERADE

ovsdb-tool create /tmp/ovs.db /usr/share/openvswitch/vswitch.ovsschema
ovsdb-server /tmp/ovs.db \
  --remote=punix:/var/run/openvswitch/db.sock \
  --pidfile=/tmp/ovs.pid --detach
ovs-vsctl --no-wait init
ovs-vswitchd --pidfile=/tmp/vswitchd.pid --detach
ovs-vsctl add-br br-int

ovsdb-tool create /tmp/nb.db /usr/share/ovn/ovn-nb.ovsschema
ovsdb-tool create /tmp/sb.db /usr/share/ovn/ovn-sb.ovsschema
ovsdb-server /tmp/nb.db \
  --remote=punix:/var/run/ovn/ovnnb_db.sock \
  --pidfile=/tmp/nb.pid --detach
ovsdb-server /tmp/sb.db \
  --remote=punix:/var/run/ovn/ovnsb_db.sock \
  --pidfile=/tmp/sb.pid --detach
ovn-nbctl init
ovn-sbctl init
ovn-northd --pidfile=/tmp/northd.pid --detach
ovs-vsctl set Open_vSwitch . \
  external_ids:system-id=teamlab-p1 \
  external_ids:ovn-remote=unix:/var/run/ovn/ovnsb_db.sock \
  external_ids:ovn-encap-type=geneve \
  external_ids:ovn-encap-ip=10.250.0.2
ovn-controller --pidfile=/tmp/controller.pid --detach

exec /opt/gzctf-agent/gzctf-agent
