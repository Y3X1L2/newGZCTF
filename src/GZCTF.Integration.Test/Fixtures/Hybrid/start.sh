#!/bin/sh
set -eu
# This isolated fixture owns its databases. No host Docker socket or host network is used.
ovsdb-tool create /tmp/ovs.db /usr/share/openvswitch/vswitch.ovsschema
ovsdb-server /tmp/ovs.db --remote=punix:/var/run/openvswitch/db.sock --remote=ptcp:6640:0.0.0.0 --pidfile=/tmp/ovs.pid --detach
ovs-vsctl --no-wait init
ovs-vswitchd --pidfile=/tmp/vswitchd.pid --detach
ovs-vsctl add-br br-int -- set bridge br-int datapath_type="${HYBRID_DATAPATH:-netdev}"
ovsdb-tool create /tmp/nb.db /usr/share/ovn/ovn-nb.ovsschema
ovsdb-tool create /tmp/sb.db /usr/share/ovn/ovn-sb.ovsschema
ovsdb-server /tmp/nb.db --remote=punix:/var/run/ovn/ovnnb_db.sock --remote=ptcp:6641:0.0.0.0 --pidfile=/tmp/nb.pid --detach
ovsdb-server /tmp/sb.db --remote=punix:/var/run/ovn/ovnsb_db.sock --pidfile=/tmp/sb.pid --detach
ovn-nbctl init
ovn-sbctl init
ovn-northd --pidfile=/tmp/northd.pid --detach
ovs-vsctl set Open_vSwitch . external_ids:system-id=hybrid-qa external_ids:ovn-remote=unix:/var/run/ovn/ovnsb_db.sock external_ids:ovn-encap-type=geneve external_ids:ovn-encap-ip=127.0.0.1
ovn-controller --pidfile=/tmp/controller.pid --detach
exec sleep infinity
