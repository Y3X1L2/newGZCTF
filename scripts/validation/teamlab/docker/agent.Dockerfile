FROM ubuntu:24.04

ARG PUBLISH_PATH
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
    ca-certificates curl dnsutils docker.io iproute2 iptables iputils-ping libicu74 libpcap0.8 \
    nftables openvswitch-switch ovn-central ovn-host procps tcpdump util-linux wireguard-tools && \
    rm -rf /var/lib/apt/lists/* && \
    mkdir -p /opt/gzctf-agent /opt/teamlab-p1 /var/lib/gzctf/images /var/lib/gzctf/teamlab \
      /var/run/openvswitch /var/run/ovn
COPY ${PUBLISH_PATH}/agent/gzctf-agent /opt/gzctf-agent/gzctf-agent
COPY scripts/validation/teamlab/docker/agent-start.sh /opt/teamlab-p1/agent-start.sh
RUN chmod 755 /opt/gzctf-agent/gzctf-agent /opt/teamlab-p1/agent-start.sh
EXPOSE 5001 32100-32120
ENTRYPOINT ["/bin/sh", "/opt/teamlab-p1/agent-start.sh"]
