#!/bin/bash
# T002: Install KVM/QEMU + libvirt on Linux server
set -e
echo "Installing KVM/QEMU + libvirt..."
sudo apt update
sudo apt install -y qemu-kvm libvirt-daemon-system libvirt-clients virtinst bridge-utils
echo "Verifying KVM availability..."
sudo kvm-ok
echo "Starting libvirt service..."
sudo systemctl enable --now libvirtd
sudo usermod -aG libvirt $USER
echo "KVM/libvirt installation complete. Please re-login for group changes to take effect."
