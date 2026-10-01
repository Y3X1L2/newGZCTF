"""Run generated TeamLab Linux networking code in a local filesystem/subprocess sandbox.

Usage: python test-managed-linux-network-script.py --script /path/to/generated-linux-apply.py
Requires PyYAML. No guest, network command or server is contacted.
"""

import argparse
import base64
import json
import pathlib
import re
import signal
import subprocess
import sys
import tempfile
import types
import unittest
from unittest import mock

import yaml


SOURCE = ""
MAC = "02:42:29:19:d6:14"


class ManagedLinuxScriptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="gzctf-teamlab-script-")
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        for name in ["netplan", "lock", "state"]:
            (self.root / name).mkdir()
        self.legacy = self.root / "netplan" / "01-template.yaml"
        self.legacy.write_text(
            "network:\n  version: 2\n  ethernets:\n"
            "    ens18:\n      addresses: [10.96.1.99/24]\n"
            "      nameservers:\n        addresses: [10.96.1.53]\n"
            "    other0:\n      match:\n        macaddress: '02:42:29:19:d6:99'\n"
            "      dhcp4: true\n"
        )
        self.original = self.legacy.read_bytes()
        payload = re.search(r"base64.b64decode\('([^']+)'\)", SOURCE).group(1)
        self.targets = json.loads(base64.b64decode(payload))
        self.targets[0].update(
            macAddress=MAC, name="ens18", ipAddress="10.96.1.20", prefixLength=24,
            gateway=None, dnsServers=[], routes=[{"destinationCidr": "172.16.0.0/16", "nextHop": "10.96.1.1", "metric": 25}]
        )
        self.fail = None
        self.calls = []

    def command(self, args, **kwargs):
        self.calls.append(args)
        if args == ["cloud-init", "status", "--wait"]:
            return "status: done"
        if args == ["ip", "-j", "link", "show"]:
            return json.dumps([
                {"ifname": "enp0s3", "address": MAC},
                {"ifname": "other0", "address": "02:42:29:19:d6:99"},
            ])
        if args in (["netplan", "generate"], ["netplan", "apply"]):
            if self.fail == args[1]:
                self.fail = None
                raise subprocess.CalledProcessError(1, args)
            return ""
        raise AssertionError("Unmocked network command: " + repr(args))

    def execute(self):
        source = SOURCE
        payload = base64.b64encode(json.dumps(self.targets).encode()).decode()
        source = re.sub(r"base64.b64decode\('[^']+'\)", "base64.b64decode('" + payload + "')", source, count=1)
        source = source.replace("/etc/netplan", (self.root / "netplan").as_posix())
        source = source.replace("/run/lock", (self.root / "lock").as_posix())
        source = source.replace("/var/lib/gzctf", (self.root / "state").as_posix())
        # fcntl is simulated on Windows; subprocess calls are simulated on every host.
        fake_fcntl = types.SimpleNamespace(LOCK_EX=1, LOCK_NB=2, flock=lambda *args: None)
        previous_handler = signal.getsignal(signal.SIGTERM)
        try:
            with mock.patch.dict(sys.modules, {"fcntl": fake_fcntl}), \
                    mock.patch("subprocess.check_output", self.command), \
                    mock.patch("shutil.which", lambda name: "/usr/bin/" + name):
                exec(compile(source, "managed-linux-network.py", "exec"), {})
        finally:
            signal.signal(signal.SIGTERM, previous_handler)

    def test_rename_removes_old_target_name_but_preserves_other_nics(self):
        before = yaml.safe_load(self.original)["network"]["ethernets"]["other0"]
        self.execute()
        config = yaml.safe_load(self.legacy.read_text())["network"]["ethernets"]
        self.assertNotIn("ens18", config)
        self.assertEqual(before, config["other0"])
        managed = yaml.safe_load((self.root / "netplan" / "90-gzctf-teamlab.yaml").read_text())["network"]["ethernets"]
        nic = managed["gzctf-" + MAC.replace(":", "")]
        self.assertEqual("ens18", nic["set-name"])
        self.assertFalse(nic["dhcp4"])
        self.assertEqual([], nic["nameservers"]["addresses"])
        self.assertEqual([{"to": "172.16.0.0/16", "via": "10.96.1.1", "metric": 25}], nic["routes"])

    def test_generate_failure_restores_original_files(self):
        self.fail = "generate"
        with self.assertRaises(subprocess.CalledProcessError):
            self.execute()
        self.assertEqual(self.original, self.legacy.read_bytes())
        self.assertFalse((self.root / "netplan" / "90-gzctf-teamlab.yaml").exists())
        self.assertIn(["netplan", "apply"], self.calls)

    def test_apply_failure_restores_original_files_and_allows_retry(self):
        self.fail = "apply"
        with self.assertRaises(subprocess.CalledProcessError):
            self.execute()
        self.assertEqual(self.original, self.legacy.read_bytes())
        self.execute()
        self.assertNotIn("ens18", yaml.safe_load(self.legacy.read_text())["network"]["ethernets"])

    def test_retry_does_not_overwrite_initial_operator_backup(self):
        self.execute()
        backup = next((self.root / "state").rglob("01-template.yaml.initial"))
        self.assertEqual(self.original, backup.read_bytes())
        self.targets[0]["dnsServers"] = ["10.96.1.54"]
        self.execute()
        self.assertEqual(self.original, backup.read_bytes())
        self.assertTrue(next((self.root / "state").rglob("90-gzctf-teamlab.yaml.absent")).exists())

    def test_failed_update_restores_last_successful_plan(self):
        self.execute()
        managed = self.root / "netplan" / "90-gzctf-teamlab.yaml"
        last_success = managed.read_bytes()
        self.targets[0]["dnsServers"] = ["10.96.1.54"]
        self.fail = "apply"
        with self.assertRaises(subprocess.CalledProcessError):
            self.execute()
        self.assertEqual(last_success, managed.read_bytes())


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--script", required=True)
    args = parser.parse_args()
    SOURCE = pathlib.Path(args.script).read_text()
    unittest.main(argv=[sys.argv[0]], verbosity=2)
