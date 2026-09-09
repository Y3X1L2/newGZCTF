FROM ubuntu:24.04
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
    ovn-central ovn-host openvswitch-switch iproute2 iputils-ping tcpdump curl \
    qemu-system-x86 busybox-static cpio linux-image-virtual && \
    mkdir -p /var/run/openvswitch /var/run/ovn
RUN DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends zstd ethtool
RUN DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends libvirt-daemon-system grub-pc-bin grub2-common parted e2fsprogs wireguard-tools qemu-utils
COPY start.sh guest-init prepare-guest.sh /opt/hybrid/
COPY prepare-boot-disk.sh /opt/hybrid/
COPY qemu.conf /etc/libvirt/qemu.conf
RUN chmod 755 /opt/hybrid/start.sh
ENTRYPOINT ["/opt/hybrid/start.sh"]
