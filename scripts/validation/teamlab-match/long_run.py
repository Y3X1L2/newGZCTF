#!/usr/bin/env python3
"""Provision two 20-asset TeamLab environments, run the two-hour load, then clean up."""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import time
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path

import requests

import runner


SCOPES = [
    "operations:read", "images:read",
    "teamlab.topologies:read", "teamlab.topologies:write",
    "teamlab.runtimes:read", "teamlab.runtimes:write",
    "teamlab.traffic:read", "teamlab.capture:read", "teamlab.capture:write",
    "teamlab.resource-pools:read", "teamlab.device-packages:read",
    "teamlab.link-policies:read", "teamlab.link-policies:write",
    "teamlab.remote-sessions:read", "teamlab.remote-sessions:write",
]

DOCKER_TEMPLATES = [53, 54, 56, 57, 58, 59, 60, 46, 473, 474, 475, 476, 477, 478]


class Match:
    def __init__(self, base: str, token: str, output: Path):
        self.base = base.rstrip("/")
        self.session = requests.Session()
        self.session.headers["Authorization"] = f"Bearer {token}"
        self.output = output
        self.marker = datetime.now().strftime("%Y%m%d-%H%M%S")
        self.scope_id = ""
        self.resources: list[dict] = []

    def request(self, method: str, path: str, body=None, key: str | None = None, timeout: int = 120):
        headers = {"Idempotency-Key": key} if key else None
        response = self.session.request(method, self.base + path, json=body, headers=headers, timeout=timeout)
        if not response.ok:
            try:
                problem = response.json()
                detail = problem.get("detail") or problem.get("message") or problem.get("code")
            except ValueError:
                detail = response.text[:300]
            raise RuntimeError(f"{method} {path}: HTTP {response.status_code} {detail}")
        return response.json() if response.content else None

    def wait_operation(self, operation_id: str, timeout: int = 1800):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            operation = self.request("GET", f"/api/open/v1/operations/{operation_id}")
            status = str(operation.get("status", "")).lower()
            if status in ("2", "succeeded"):
                return operation
            if status in ("3", "failed", "4", "cancelled"):
                raise RuntimeError(f"operation {operation_id}: {operation.get('errorCode')} {operation.get('errorDetail')}")
            time.sleep(1)
        raise TimeoutError(f"operation {operation_id} timeout")

    def wait_runtime(self, runtime_id: str, wanted: set[str], timeout: int = 1800):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            runtime = self.request("GET", f"/api/open/v1/teamlab/runtimes/{runtime_id}")
            status = str(runtime.get("status", "")).lower()
            if status in wanted:
                return runtime
            if status in ("6", "failed"):
                raise RuntimeError(f"runtime {runtime_id} failed: {runtime.get('failure')}")
            time.sleep(2)
        raise TimeoutError(f"runtime {runtime_id} timeout")

    @staticmethod
    def topology(name: str, scope_id: str, pool_base: int) -> dict:
        networks = []
        for index, key in enumerate(("entry", "service", "core", "industrial")):
            networks.append({
                "key": key, "name": key.title(), "isEntry": index == 0, "orderIndex": index,
                "addressPool": {"poolCidr": f"10.{pool_base + index}.0.0/16", "runtimePrefixLength": 24},
            })
        placements = ["entry"] * 6 + ["service"] * 8
        assets = []
        offsets = {key: 10 for key in ("entry", "service", "core", "industrial")}
        for index, template_id in enumerate(DOCKER_TEMPLATES):
            network = placements[index]
            key = f"service-{index + 1}"
            assets.append({
                "key": key, "name": f"Service {index + 1}", "kind": 0,
                "imageTemplateId": template_id,
                "resources": {"cpuUnits": 1, "memoryMiB": 192, "storageMiB": 768},
                "interfaces": [{"key": "eth0", "networkKey": network, "hostOffset": offsets[network],
                                "primary": True, "orderIndex": 0}],
                "healthCheck": None, "orderIndex": len(assets),
            })
            offsets[network] += 1
        for index, template_id in enumerate((59, 60)):
            assets.append({
                "key": f"client-{index + 1}", "name": f"Traffic Client {index + 1}", "kind": 0,
                "imageTemplateId": template_id,
                "resources": {"cpuUnits": 1, "memoryMiB": 128, "storageMiB": 1024},
                "interfaces": [{"key": "eth0", "networkKey": "core", "hostOffset": offsets["core"],
                                "primary": True, "orderIndex": 0}],
                "healthCheck": None, "orderIndex": len(assets),
            })
            offsets["core"] += 1
        for index in range(2):
            assets.append({
                "key": f"modbus-{index + 1}", "name": f"Modbus PLC {index + 1}", "kind": 0,
                "imageTemplateId": 487, "devicePackageId": 1,
                "deviceParameters": {"unitId": 1, "holdingRegisters": [12, 34, 56, 78]},
                "resources": {"cpuUnits": 1, "memoryMiB": 128, "storageMiB": 1024},
                "interfaces": [{"key": "eth0", "networkKey": "industrial", "hostOffset": offsets["industrial"],
                                "primary": True, "orderIndex": 0}],
                "healthCheck": None, "orderIndex": len(assets),
            })
            offsets["industrial"] += 1
        for key, name, template_id, memory, storage in (
            ("linux-vm", "Linux VM", 115, 1024, 8192),
            ("windows-vm", "Windows VM", 121, 2048, 20480),
        ):
            assets.append({
                "key": key, "name": name, "kind": 1, "imageTemplateId": template_id,
                "resources": {"cpuUnits": 1 if key == "linux-vm" else 2,
                              "memoryMiB": memory, "storageMiB": storage},
                "interfaces": [{"key": "eth0", "networkKey": "core", "hostOffset": offsets["core"],
                                "primary": True, "orderIndex": 0}],
                "healthCheck": None, "orderIndex": len(assets),
            })
            offsets["core"] += 1
        return {
            "name": name, "controlScopeId": scope_id, "schemaVersion": 2,
            "networks": networks, "assets": assets,
            "connections": [],
            "infrastructure": [],
            "observation": {"flowMetadataEnabled": True, "onDemandPcapEnabled": True},
        }

    def provision(self, team: str, pool_base: int) -> dict:
        topology_body = self.topology(f"Match {team} {self.marker}", self.scope_id, pool_base)
        operation = self.request("POST", "/api/open/v1/teamlab/topologies", topology_body,
                                 f"match-{self.marker}-{team}-topology")
        completed = self.wait_operation(operation["id"])
        topology_id = completed.get("resourceId") or completed.get("result", {}).get("resourceId")
        validation = self.request("POST", f"/api/open/v1/teamlab/topologies/{topology_id}/validate")
        if not validation.get("valid"):
            raise RuntimeError(f"{team} topology invalid: {validation.get('issues')}")
        detail = self.request("GET", f"/api/open/v1/teamlab/topologies/{topology_id}")
        operation = self.request("POST", f"/api/open/v1/teamlab/topologies/{topology_id}/releases",
                                 {"revision": detail["revision"]}, f"match-{self.marker}-{team}-release")
        self.wait_operation(operation["id"])
        release_id = self.request("GET", f"/api/open/v1/teamlab/topologies/{topology_id}/releases?limit=10")["items"][0]["id"]
        operation = self.request("POST", f"/api/open/v1/teamlab/preparations/releases/{release_id}",
                                 key=f"match-{self.marker}-{team}-prepare", timeout=180)
        self.wait_operation(operation["id"], 2400)
        deadline = time.monotonic() + 900
        while time.monotonic() < deadline:
            preparation = self.request("GET", f"/api/open/v1/teamlab/preparations/releases/{release_id}")
            if preparation.get("readyToStart"):
                break
            if preparation.get("state") == "blocked":
                raise RuntimeError(f"{team} preparation blocked: {preparation.get('blockers')}")
            time.sleep(3)
        else:
            raise TimeoutError(f"{team} preparation timeout")
        operation = self.request("POST", "/api/open/v1/teamlab/runtimes", {
            "releaseId": release_id, "externalReference": f"match-{self.marker}-{team}",
            "constraints": None, "overlays": [],
        }, f"match-{self.marker}-{team}-runtime")
        completed = self.wait_operation(operation["id"], 2400)
        runtime_id = completed.get("resourceId") or completed.get("result", {}).get("resourceId")
        runtime = self.wait_runtime(runtime_id, {"5", "running"}, 1800)
        if len(runtime.get("assets", [])) != 20:
            raise RuntimeError(f"{team} expected 20 assets, got {len(runtime.get('assets', []))}")
        by_key = {asset["key"]: asset for asset in runtime["assets"]}
        web = self.request("POST", f"/api/open/v1/teamlab/runtimes/{runtime_id}/assets/{by_key['service-1']['id']}/service-access",
                           {"protocol": "tcp", "internalPort": 80, "publicPort": None, "networkKey": "entry"})
        modbus = self.request("POST", f"/api/open/v1/teamlab/runtimes/{runtime_id}/assets/{by_key['modbus-1']['id']}/service-access",
                              {"protocol": "tcp", "internalPort": 1502, "publicPort": None, "networkKey": "industrial"})
        for access in (web, modbus):
            if str(access.get("status", "")).lower() != "active":
                raise RuntimeError(access.get("lastError") or f"{team} service access failed")
        resource = {
            "team": team, "topologyId": topology_id, "releaseId": release_id,
            "runtimeId": runtime_id, "web": web["endpoint"], "modbus": modbus["endpoint"],
            "linuxVmId": by_key["linux-vm"]["id"], "windowsVmId": by_key["windows-vm"]["id"],
            "restartAssetId": by_key["service-2"]["id"], "generation": runtime["generation"],
        }
        self.resources.append(resource)
        self.write_lifecycle("provisioning")
        return resource

    def write_lifecycle(self, status: str, error: str | None = None):
        self.output.mkdir(parents=True, exist_ok=True)
        (self.output / "lifecycle.json").write_text(json.dumps({
            "status": status, "marker": self.marker, "scopeId": self.scope_id,
            "resources": self.resources, "error": error,
            "updatedAt": datetime.now(timezone.utc).isoformat(),
        }, ensure_ascii=False, indent=2), encoding="utf-8")

    def cleanup(self):
        errors = []
        for resource in reversed(self.resources):
            runtime_id = resource["runtimeId"]
            try:
                operation = self.request("DELETE", f"/api/open/v1/teamlab/runtimes/{runtime_id}",
                                         key=f"match-{self.marker}-{resource['team']}-destroy")
                self.wait_operation(operation["id"], 1800)
                self.wait_runtime(runtime_id, {"10", "destroyed"}, 900)
            except Exception as exc:
                errors.append(f"{resource['team']} runtime: {exc}")
            try:
                self.request("DELETE", f"/api/open/v1/teamlab/preparations/releases/{resource['releaseId']}",
                             key=f"match-{self.marker}-{resource['team']}-unprepare")
            except Exception as exc:
                errors.append(f"{resource['team']} preparation: {exc}")
        if self.scope_id:
            try:
                self.request("POST", f"/api/open/v1/teamlab/scopes/{self.scope_id}/archive")
            except Exception as exc:
                errors.append(f"scope: {exc}")
        self.write_lifecycle("cleanup_failed" if errors else "completed", "; ".join(errors) or None)


async def run_load(match: Match, teams: list[dict], duration: int, state: runner.State):
    recover_at = (datetime.now(timezone.utc) + timedelta(seconds=2220)).isoformat()
    config = {"name": "TeamLab 双队 40 资产比赛仿真", "baseUrl": match.base,
              "auth": {"bearerToken": match.session.headers["Authorization"].split(" ", 1)[1]},
              "teams": [], "monitors": [], "stages": []}
    for item in teams:
        web_host, web_port = item["web"].rsplit(":", 1)
        modbus_host, modbus_port = item["modbus"].rsplit(":", 1)
        config["teams"].append({
            "name": item["team"], "virtualUsers": 25, "burstUsers": 50,
            "probes": [
                {"name": "web", "type": "http", "url": f"http://{web_host}:{web_port}/", "expectStatus": 200},
                {"name": "modbus", "type": "modbus", "host": modbus_host, "port": int(modbus_port),
                 "unitId": 1, "startAddress": 0, "quantity": 4,
                 "expectedRegisters": [12, 34, 56, 78]},
            ],
        })
        runtime_id = item["runtimeId"]
        config["monitors"].append({"name": f"runtime-{item['team']}",
                                   "path": f"/api/open/v1/teamlab/runtimes/{runtime_id}/status",
                                   "intervalSeconds": 5, "select": ["status", "stage", "assetSummary"]})
    red, blue = teams
    config["stages"] = [
        {"name": "环境核对", "atSeconds": 0, "actions": [
            {"name": item["team"], "method": "GET", "path": f"/api/open/v1/teamlab/runtimes/{item['runtimeId']}/status-check"}
            for item in teams]},
        {"name": "平稳访问", "atSeconds": 600, "actions": [
            {"name": item["team"], "method": "GET", "path": f"/api/open/v1/teamlab/runtimes/{item['runtimeId']}/status"}
            for item in teams]},
        {"name": "链路策略", "atSeconds": 2100, "actions": [
            {"name": item["team"], "method": "POST", "path": "/api/open/v1/teamlab/link-policies",
             "body": {"runtimeId": item["runtimeId"], "networkKey": "service", "assetKey": None,
                      "kind": "latency", "parameters": {"delayMillis": 80}, "recoverAt": recover_at}}
            for item in teams]},
        {"name": "抓包与工控流量", "atSeconds": 3000, "actions": [
            {"name": item["team"], "method": "POST",
             "path": f"/api/open/v1/teamlab/runtimes/{item['runtimeId']}/captures",
             "headers": {"Idempotency-Key": f"match-{match.marker}-{item['team']}-capture"},
             "body": {"scope": "network", "networkKey": "industrial", "maxSeconds": 120,
                      "maxBytes": 33554432, "expiresInSeconds": 7200}}
            for item in teams]},
        {"name": "资产重启", "atSeconds": 3900, "actions": [
            {"name": item["team"], "method": "POST",
             "path": f"/api/open/v1/teamlab/runtimes/{item['runtimeId']}/assets/{item['restartAssetId']}/control",
             "headers": {"Idempotency-Key": f"match-{match.marker}-{item['team']}-restart"},
             "body": {"generation": item["generation"], "action": "restart",
                      "reason": "两小时比赛仿真资产重启", "confirmed": True}}
            for item in teams]},
        {"name": "VNC 运维会话", "atSeconds": 4800, "actions": [
            {"name": item["team"], "method": "POST",
             "path": f"/api/open/v1/teamlab/runtimes/{item['runtimeId']}/assets/{item['windowsVmId']}/remote-sessions",
             "headers": {"Idempotency-Key": f"match-{match.marker}-{item['team']}-vnc"},
             "body": {"reason": "两小时比赛仿真 VNC 验证", "vncConsole": True}}
            for item in teams]},
        {"name": "恢复核对", "atSeconds": 5700, "actions": [
            {"name": item["team"], "method": "GET", "path": f"/api/open/v1/teamlab/runtimes/{item['runtimeId']}/status-check"}
            for item in teams]},
        {"name": "最终核对", "atSeconds": 6900, "actions": [
            {"name": item["team"], "method": "GET", "path": f"/api/open/v1/teamlab/runtimes/{item['runtimeId']}"}
            for item in teams]},
    ]
    state.name = config["name"]
    state.duration = duration
    state.started = time.monotonic()
    state.stages = [{"name": stage["name"], "status": "pending"} for stage in config["stages"]]
    match.write_lifecycle("running")
    await runner.orchestrate(config, state, match.output)


async def run_match(match: Match, args: argparse.Namespace) -> None:
    state = runner.State("TeamLab 双队 40 资产比赛仿真", args.duration)
    state.phase = "环境准备中"
    server = await runner.serve(state, args.listen)
    publisher = asyncio.create_task(runner.periodic_publish(state, match.output))
    print(f"看板: http://{args.listen}/", flush=True)
    try:
        scope = await asyncio.to_thread(match.request, "POST", "/api/open/v1/teamlab/scopes", {
            "key": f"match-{match.marker.lower()}", "displayName": f"双队比赛仿真 {match.marker}"})
        match.scope_id = scope["id"]
        match.write_lifecycle("provisioning")
        red = await asyncio.to_thread(match.provision, "red", 160)
        blue = await asyncio.to_thread(match.provision, "blue", 170)
        await run_load(match, [red, blue], args.duration, state)
    except Exception as exc:
        state.phase = "测试失败"
        state.finished = True
        state.failure("framework", "provisioning", str(exc))
        match.write_lifecycle("failed", str(exc))
        raise
    finally:
        await asyncio.to_thread(match.cleanup)
        publisher.cancel()
        await asyncio.gather(publisher, return_exceptions=True)
        await server.cleanup()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://10.24.0.27:8080")
    parser.add_argument("--listen", default="0.0.0.0:19090")
    parser.add_argument("--duration", type=int, default=7200)
    parser.add_argument("--output", default="artifacts/teamlab-match/formal-latest")
    args = parser.parse_args()
    token = os.environ.get("GZCTF_API_TOKEN", "")
    if not token:
        raise RuntimeError("GZCTF_API_TOKEN is required")
    match = Match(args.base_url, token, Path(args.output))
    asyncio.run(run_match(match, args))


if __name__ == "__main__":
    main()
