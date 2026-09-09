#!/bin/sh
set -eu
# Builds a disposable BIOS-bootable Linux disk for the production libvirt execution path.
# Loop devices are allocated by losetup and detached in this same shell.
sh /opt/hybrid/prepare-guest.sh
# Platform offsets 1-3 are reserved for network services.
sed -i 's@10.83.0.3/24@10.83.0.20/24@' /tmp/guest/init
kernel=$(find /lib/modules -mindepth 1 -maxdepth 1 -type d -printf '%f\n' | head -1)
mkdir -p /tmp/guest/modules /tmp/boot-disk-mount
: > /tmp/guest/load-modules
for driver in virtio_pci virtio_net; do
    modprobe -S "$kernel" --show-depends "$driver" | while read -r action module remainder; do
        test "$action" = insmod || continue
        name=$(basename "$module" .zst)
        grep -Fxq "insmod /modules/$name" /tmp/guest/load-modules && continue
        case "$module" in
            *.zst) zstd -dc "$module" > "/tmp/guest/modules/$name" ;;
            *) cp "$module" "/tmp/guest/modules/$name" ;;
        esac
        printf 'insmod /modules/%s\n' "$name" >> /tmp/guest/load-modules
    done
done
(cd /tmp/guest && find . -print0 | cpio --null -o --format=newc | gzip -1 > /tmp/guest.cpio.gz)
truncate -s 96M /tmp/boot-disk.raw
parted -s /tmp/boot-disk.raw mklabel msdos mkpart primary ext2 1MiB 100% set 1 boot on
loop=$(losetup --find --show --partscan /tmp/boot-disk.raw)
case "$loop" in /dev/loop[0-9]*) ;; *) exit 1 ;; esac
mounted=false
created_node=false
partition="${loop}p1"
cleanup() { if "$mounted"; then umount /tmp/boot-disk-mount; fi; losetup -d "$loop"; if "$created_node"; then rm "$partition"; fi; }
trap cleanup EXIT
test "$(losetup -n -O BACK-FILE "$loop")" = /tmp/boot-disk.raw
if [ ! -b "$partition" ]; then
    device=$(cat "/sys/class/block/$(basename "$partition")/dev")
    mknod "$partition" b "${device%:*}" "${device#*:}"
    created_node=true
fi
mkfs.ext2 -F "$partition"
mount "$partition" /tmp/boot-disk-mount
mounted=true
mkdir -p /tmp/boot-disk-mount/boot/grub
cp /boot/vmlinuz-* /tmp/boot-disk-mount/boot/vmlinuz
cp /tmp/guest.cpio.gz /tmp/boot-disk-mount/boot/initrd
printf 'set timeout=0\nmenuentry "Hybrid QA" {\n linux /boot/vmlinuz console=ttyS0 rdinit=/init panic=-1\n initrd /boot/initrd\n}\n' > /tmp/boot-disk-mount/boot/grub/grub.cfg
grub-install --target=i386-pc --boot-directory=/tmp/boot-disk-mount/boot --no-floppy "$loop"
umount /tmp/boot-disk-mount
mounted=false
losetup -d "$loop"
if "$created_node"; then rm "$partition"; fi
trap - EXIT
qemu-img convert -f raw -O qcow2 /tmp/boot-disk.raw /tmp/hybrid-guest.qcow2
sha256sum /tmp/hybrid-guest.qcow2
