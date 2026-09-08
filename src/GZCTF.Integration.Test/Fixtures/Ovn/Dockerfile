FROM ubuntu:24.04
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends ovn-central openvswitch-common && mkdir -p /var/run/openvswitch /var/run/ovn
CMD ["sh", "-c", "ovsdb-tool create /tmp/ovn.db /usr/share/ovn/ovn-nb.ovsschema && exec ovsdb-server --remote=punix:/tmp/nb.sock --pidfile=/tmp/ovn.pid --no-chdir /tmp/ovn.db"]
