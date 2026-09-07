import importlib.util
import json
from pathlib import Path
import subprocess
import unittest


spec = importlib.util.spec_from_file_location("restore_guest_network", Path(__file__).with_name("restore-agent-guest-network.py"))
network = importlib.util.module_from_spec(spec)
spec.loader.exec_module(network)


class GuestNetworkTests(unittest.TestCase):
    def setUp(self):
        self.configuration = {"Agent": {"GuestManagement": {"enabled": True}}}
        self.calls = []
        self.links = []
        self.addresses = []

    def run_command(self, command, **kwargs):
        self.calls.append((command, kwargs))
        value = self.links if "link" in command else self.addresses
        return subprocess.CompletedProcess(command, 0, json.dumps(value), "")

    def test_disabled_configuration_does_not_touch_network(self):
        self.assertFalse(network.restore({}, self.run_command))
        self.assertEqual([], self.calls)

    def test_missing_bridge_is_restored_after_firewall(self):
        self.assertTrue(network.restore(self.configuration, self.run_command))
        commands = [command for command, _ in self.calls]
        self.assertLess(commands.index(["nft", "-f", "-"]), commands.index(["ip", "link", "add", "gzmgt0", "type", "bridge"]))
        self.assertIn(["ip", "address", "replace", "100.127.0.1/16", "dev", "gzmgt0"], commands)
        rules = self.calls[3][1]["input"]
        self.assertIn('iifname "gzmgt0" drop', rules)
        self.assertIn('oifname "gzmgt0" drop', rules)
        self.assertNotIn("flush ruleset", rules)

    def test_existing_bridge_is_not_recreated(self):
        self.links = [{"ifname": "gzmgt0", "linkinfo": {"info_kind": "bridge"}}]
        network.restore(self.configuration, self.run_command)
        self.assertFalse(any("add" in command for command, _ in self.calls))

    def test_conflicting_interface_is_rejected_before_mutation(self):
        self.links = [{"ifname": "gzmgt0", "linkinfo": {"info_kind": "veth"}}]
        with self.assertRaises(ValueError):
            network.restore(self.configuration, self.run_command)
        self.assertEqual(1, len(self.calls))

    def test_address_owned_by_another_interface_is_rejected(self):
        self.addresses = [{"ifname": "ens19", "addr_info": [{"local": "100.127.0.1"}]}]
        with self.assertRaises(ValueError):
            network.restore(self.configuration, self.run_command)
        self.assertEqual(2, len(self.calls))

    def test_invalid_bridge_is_rejected(self):
        self.configuration["Agent"]["GuestManagement"]["bridgeName"] = 'gzmgt0"; reboot'
        with self.assertRaises(ValueError):
            network.restore(self.configuration, self.run_command)
        self.assertEqual([], self.calls)


if __name__ == "__main__":
    unittest.main()
