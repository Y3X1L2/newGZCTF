import importlib.util
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path

from aiohttp import web


MODULE_PATH = Path(__file__).with_name("runner.py")
SPEC = importlib.util.spec_from_file_location("teamlab_match_runner", MODULE_PATH)
runner = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = runner
SPEC.loader.exec_module(runner)


class RunnerTests(unittest.TestCase):
    def test_percentile_uses_observed_value(self):
        self.assertEqual(40, runner.percentile([10, 20, 30, 40, 50], .75))
        self.assertEqual(0, runner.percentile([], .95))

    def test_environment_expansion_is_recursive(self):
        os.environ["TEAMLAB_MATCH_TEST"] = "value"
        self.assertEqual(
            {"items": ["value"]},
            runner.expand({"items": ["${TEAMLAB_MATCH_TEST}"]}),
        )

    def test_missing_environment_variable_fails_before_requests(self):
        os.environ.pop("TEAMLAB_MATCH_MISSING", None)
        with self.assertRaisesRegex(ValueError, "TEAMLAB_MATCH_MISSING"):
            runner.expand("${TEAMLAB_MATCH_MISSING}")

    def test_failures_are_aggregated(self):
        state = runner.State("test", 10)
        state.failure("api", "stage:test", "HTTP 500")
        state.failure("api", "stage:test", "HTTP 500")
        self.assertEqual(1, len(state.errors))
        self.assertEqual(2, next(iter(state.errors.values()))["count"])


class RunnerFlowTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        app = web.Application()
        async def ready(_):
            return web.json_response({"ready": True})

        async def status(_):
            return web.json_response({"status": "running"})

        app.add_routes([
            web.get("/asset", ready),
            web.get("/status", status),
        ])
        self.server = web.AppRunner(app)
        await self.server.setup()
        self.site = web.TCPSite(self.server, "127.0.0.1", 0)
        await self.site.start()
        port = self.site._server.sockets[0].getsockname()[1]
        self.base_url = f"http://127.0.0.1:{port}"

    async def asyncTearDown(self):
        await self.server.cleanup()

    async def test_short_run_writes_real_report(self):
        config = {
            "baseUrl": self.base_url,
            "auth": {},
            "teams": [{
                "name": "red", "virtualUsers": 1, "burstUsers": 1,
                "probes": [{"name": "asset", "type": "http", "url": self.base_url + "/asset"}],
            }],
            "monitors": [{"name": "runtime", "path": "/status", "intervalSeconds": .1, "select": ["status"]}],
            "stages": [{"name": "read", "atSeconds": 0, "actions": [{"name": "status", "method": "GET", "path": "/status"}]}],
        }
        state = runner.State("flow", 1)
        state.stages = [{"name": "read", "status": "pending"}]
        with tempfile.TemporaryDirectory() as directory:
            await runner.orchestrate(config, state, Path(directory))
            report = json.loads((Path(directory) / "report.json").read_text(encoding="utf-8"))
            self.assertTrue(report["finished"])
            self.assertGreater(report["metrics"]["total"], 0)
            self.assertEqual("passed", report["stages"][0]["status"])
            self.assertEqual("running", report["monitors"]["runtime"]["status"])


if __name__ == "__main__":
    unittest.main()
