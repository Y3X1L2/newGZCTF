#!/usr/bin/env python3
"""Restore the configured Agent guest network before its HTTPS listener starts."""

import argparse
import ipaddress
import json
from pathlib import Path
import re
import subprocess


def guest_network(configuration):
    agent = {key.lower(): value for key, value in configuration.get("Agent", {}).items()}
    settings = {key.lower(): value for key, value in agent.get("guestmanagement", {}).items()}
    if not settings.get("enabled", False):
        return None
    bridge = settings.get("bridgename", "gzmgt0")
    address = ipaddress.IPv4Address(settings.get("hostaddress", "100.127.0.1"))
    prefix = settings.get("prefixlength", 16)
    port = settings.get("listenport", 5443)
    if not re.fullmatch(r"[A-Za-z0-9_.-]{1,15}", bridge) or bridge in ("lo", ".", ".."):
        raise ValueError("Invalid guest management bridge name")
    if prefix != 16 or not isinstance(port, int) or not 1 <= port <= 65535:
        raise ValueError("Invalid guest management prefix or port")
    return bridge, str(address), prefix, port


def firewall_rules(bridge, port):
    return f'''destroy table inet gzctf_guest_mgmt
table inet gzctf_guest_mgmt {{
  chain input {{
    type filter hook input priority -10; policy accept;
    iifname "{bridge}" ct state established,related accept
    iifname "{bridge}" udp dport 67 accept
    iifname "{bridge}" tcp dport {port} accept
    iifname "{bridge}" drop
  }}
  chain forward {{
    type filter hook forward priority -10; policy accept;
    iifname "{bridge}" drop
    oifname "{bridge}" drop
  }}
}}
'''


def restore(configuration, run=subprocess.run):
    settings = guest_network(configuration)
    if settings is None:
        return False
    bridge, address, prefix, port = settings
    links = json.loads(run(["ip", "-json", "-details", "link", "show"],
                           check=True, capture_output=True, text=True).stdout)
    existing = next((link for link in links if link["ifname"] == bridge), None)
    if existing and existing.get("linkinfo", {}).get("info_kind") != "bridge":
        raise ValueError("Configured guest interface exists but is not a bridge")
    addresses = json.loads(run(["ip", "-json", "-4", "address", "show"],
                               check=True, capture_output=True, text=True).stdout)
    for link in addresses:
        if link["ifname"] != bridge and any(info.get("local") == address for info in link.get("addr_info", [])):
            raise ValueError("Guest management address already belongs to another interface")
    rules = firewall_rules(bridge, port)
    run(["nft", "--check", "-f", "-"], input=rules, text=True, check=True)
    # The nft batch replaces only our table atomically, before bringing the bridge up.
    run(["nft", "-f", "-"], input=rules, text=True, check=True)
    if existing is None:
        run(["ip", "link", "add", bridge, "type", "bridge"], check=True)
    run(["ip", "address", "replace", f"{address}/{prefix}", "dev", bridge], check=True)
    run(["ip", "link", "set", bridge, "up"], check=True)
    return True


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("configuration", type=Path)
    args = parser.parse_args()
    restore(json.loads(args.configuration.read_text(encoding="utf-8-sig")))
