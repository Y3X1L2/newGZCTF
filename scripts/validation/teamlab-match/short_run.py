#!/usr/bin/env python3
"""生产比赛仿真前的短流程检查。

创建一个独立的三资产混合环境，经正式 Open API、部署队列和 Agent 执行面完成
发布、预热、运行、Modbus 访问及销毁。管理密码只从终端读取。
"""

from __future__ import annotations

import argparse
import getpass
import json
import socket
import struct
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import requests


SCOPES = [
    "operations:read",
    "images:read",
    "teamlab.topologies:read", "teamlab.topologies:write",
    "teamlab.runtimes:read", "teamlab.runtimes:write",
    "teamlab.traffic:read", "teamlab.capture:read", "teamlab.capture:write",
    "teamlab.resource-pools:read", "teamlab.device-packages:read",
    "teamlab.link-policies:read", "teamlab.link-policies:write",
    "teamlab.remote-sessions:read", "teamlab.remote-sessions:write",
]


def now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


class ShortRun:
    def __init__(self, base_url: str, user: str, password: str, output: Path):
        self.base = base_url.rstrip("/")
        self.admin = requests.Session()
        self.admin.post(
            self.base + "/api/account/login",
            json={"userName": user, "password": password}, timeout=20,
        ).raise_for_status()
        profile = self.admin.get(self.base + "/api/account/profile", timeout=20)
        profile.raise_for_status()
        if profile.json().get("role") not in (3, "Admin", "SuperAdmin"):
            raise RuntimeError("当前账号不是平台管理员")
        self.token = ""
        self.token_id = ""
        self.scope_id = ""
        self.topology_id = ""
        self.release_id = ""
        self.runtime_id = ""
        self.runtime_destroyed = False
        self.output = output
        self.events: list[dict[str, Any]] = []

    def event(self, stage: str, detail: str, **data: Any) -> None:
        item = {"at": now(), "stage": stage, "detail": detail, **data}
        self.events.append(item)
        print(f"[{stage}] {detail}", flush=True)

    def api(self, method: str, path: str, body: Any = None, *, key: str | None = None, timeout: int = 60) -> Any:
        headers = {"Authorization": f"Bearer {self.token}"}
        if key:
            headers["Idempotency-Key"] = key
        response = requests.request(
            method, self.base + path, headers=headers, json=body, timeout=timeout,
        )
        if not response.ok:
            try:
                problem = response.json()
                detail = problem.get("detail") or problem.get("code") or response.reason
            except ValueError:
                detail = response.reason
            raise RuntimeError(f"{method} {path}: HTTP {response.status_code} {detail}")
        return response.json() if response.content else None

    def issue_token(self, marker: str) -> None:
        response = self.admin.post(self.base + "/api/tokens", json={
            "name": f"TeamLab competition preflight {marker}",
            "scopes": SCOPES,
            "resources": [{"resourceType": "teamlab-scope", "resourceId": "*"}],
            "requestsPerMinute": 5000,
        }, timeout=30)
        response.raise_for_status()
        issued = response.json()
        self.token = issued["plainTextToken"]
        self.token_id = issued["info"]["id"]
        self.event("auth", "临时受限 Token 已签发")

    def wait_operation(self, operation_id: str, timeout: int = 900) -> dict[str, Any]:
        deadline = time.monotonic() + timeout
        last = None
        while time.monotonic() < deadline:
            operation = self.api("GET", f"/api/open/v1/operations/{operation_id}")
            if operation.get("resourceType") == "teamlab-runtime" and operation.get("resourceId"):
                self.runtime_id = operation["resourceId"]
            state = (operation.get("status"), operation.get("stage"))
            if state != last:
                self.event("operation", f"{operation_id[:8]} {state[1]}", status=state[0])
                last = state
            if str(operation.get("status")).lower() in ("2", "succeeded"):
                return operation
            if str(operation.get("status")).lower() in ("3", "failed", "4", "cancelled"):
                raise RuntimeError(
                    f"操作失败 {operation.get('stage')}: "
                    f"{operation.get('errorCode')} {operation.get('errorDetail')}"
                )
            time.sleep(.5)
        raise TimeoutError(f"操作 {operation_id} 超时")

    def wait_runtime(self, states: set[str], timeout: int = 900) -> dict[str, Any]:
        deadline = time.monotonic() + timeout
        last = None
        while time.monotonic() < deadline:
            runtime = self.api("GET", f"/api/open/v1/teamlab/runtimes/{self.runtime_id}")
            state = (str(runtime.get("status")).lower(), runtime.get("stage"))
            if state != last:
                self.event("runtime", f"{state[1]}", status=state[0])
                last = state
            if state[0] in states:
                return runtime
            if state[0] in ("6", "failed"):
                failure = runtime.get("failure") or {}
                raise RuntimeError(f"运行失败: {failure.get('code')} {failure.get('detail')}")
            time.sleep(1)
        raise TimeoutError(f"运行环境未在 {timeout} 秒内进入 {states}")

    def run(self) -> None:
        marker = datetime.now().strftime("%Y%m%d-%H%M%S")
        self.issue_token(marker)
        scope = self.api("POST", "/api/open/v1/teamlab/scopes", {
            "key": f"match-preflight-{marker.lower()}",
            "displayName": f"比赛仿真短流程 {marker}",
        })
        self.scope_id = scope["id"]
        self.event("scope", "独立控制范围已创建", scopeId=self.scope_id)

        topology = {
            "name": f"比赛仿真短流程 {marker}",
            "controlScopeId": self.scope_id,
            "schemaVersion": 2,
            "networks": [{
                "key": "lab", "name": "混合测试网段", "isEntry": True, "orderIndex": 0,
                "addressPool": {"poolCidr": "10.231.0.0/16", "runtimePrefixLength": 24},
            }],
            "assets": [
                {
                    "key": "modbus", "name": "Modbus PLC", "kind": 0,
                    "imageTemplateId": 487, "devicePackageId": 1,
                    "deviceParameters": {"unitId": 7, "holdingRegisters": [12, 34, 56, 78]},
                    "resources": {"cpuUnits": 1, "memoryMiB": 128, "storageMiB": 1024},
                    "interfaces": [{"key": "eth0", "networkKey": "lab", "hostOffset": 10, "primary": True, "orderIndex": 0}],
                    "healthCheck": None, "orderIndex": 0,
                },
                {
                    "key": "linux-vm", "name": "Linux VM", "kind": 1,
                    "imageTemplateId": 115,
                    "resources": {"cpuUnits": 1, "memoryMiB": 1024, "storageMiB": 8192},
                    "interfaces": [{"key": "eth0", "networkKey": "lab", "hostOffset": 20, "primary": True, "orderIndex": 0}],
                    "healthCheck": None, "orderIndex": 1,
                },
                {
                    "key": "windows-vm", "name": "Windows VM", "kind": 1,
                    "imageTemplateId": 121,
                    "resources": {"cpuUnits": 2, "memoryMiB": 2048, "storageMiB": 20480},
                    "interfaces": [{"key": "eth0", "networkKey": "lab", "hostOffset": 30, "primary": True, "orderIndex": 0}],
                    "healthCheck": None, "orderIndex": 2,
                },
            ],
            "connections": [],
            "infrastructure": [],
            "observation": {"flowMetadataEnabled": True, "onDemandPcapEnabled": True},
        }
        submitted = self.api("POST", "/api/open/v1/teamlab/topologies", topology, key=f"preflight-topology-{marker}")
        operation = self.wait_operation(submitted["id"])
        self.topology_id = operation.get("resourceId") or operation.get("result", {}).get("resourceId")
        self.event("topology", "三资产混合拓扑已创建", topologyId=self.topology_id)

        validation = self.api("POST", f"/api/open/v1/teamlab/topologies/{self.topology_id}/validate")
        if not validation.get("valid"):
            raise RuntimeError(f"拓扑校验失败: {validation.get('issues')}")
        detail = self.api("GET", f"/api/open/v1/teamlab/topologies/{self.topology_id}")
        submitted = self.api(
            "POST", f"/api/open/v1/teamlab/topologies/{self.topology_id}/releases",
            {"revision": detail["revision"]}, key=f"preflight-release-{marker}",
        )
        self.wait_operation(submitted["id"])
        releases = self.api("GET", f"/api/open/v1/teamlab/topologies/{self.topology_id}/releases?limit=10")
        self.release_id = releases["items"][0]["id"]
        plan = self.api("POST", f"/api/open/v1/teamlab/topologies/{self.topology_id}/releases/{self.release_id}/plan")
        self.event("release", "发布和执行计划生成成功", releaseId=self.release_id, shards=len(plan.get("shards", [])))

        submitted = self.api(
            "POST", f"/api/open/v1/teamlab/preparations/releases/{self.release_id}",
            key=f"preflight-prepare-{marker}", timeout=120,
        )
        self.wait_operation(submitted["id"], 1200)
        deadline = time.monotonic() + 300
        while time.monotonic() < deadline:
            preparation = self.api("GET", f"/api/open/v1/teamlab/preparations/releases/{self.release_id}")
            if preparation.get("readyToStart"):
                break
            if preparation.get("state") == "blocked":
                raise RuntimeError(f"镜像准备受阻: {preparation.get('blockers')}")
            time.sleep(1)
        else:
            raise TimeoutError("镜像准备未完成")
        self.event("prepare", "三类镜像均已准备")

        submitted = self.api("POST", "/api/open/v1/teamlab/runtimes", {
            "releaseId": self.release_id,
            "externalReference": f"match-preflight-{marker}",
            "constraints": None, "overlays": [],
        }, key=f"preflight-runtime-{marker}")
        self.runtime_id = submitted.get("resourceId") or ""
        operation = self.wait_operation(submitted["id"], 1200)
        self.runtime_id = operation.get("resourceId") or operation.get("result", {}).get("resourceId")
        runtime = self.wait_runtime({"5", "running"}, 1200)
        if len(runtime.get("assets", [])) != 3:
            raise RuntimeError("运行环境没有返回三个资产")
        modbus = next(asset for asset in runtime["assets"] if asset["key"] == "modbus")
        self.event("deploy", "Docker、Linux VM、Windows VM 均已运行", runtimeId=self.runtime_id)

        access = self.api(
            "POST", f"/api/open/v1/teamlab/runtimes/{self.runtime_id}/assets/{modbus['id']}/service-access",
            {"protocol": "tcp", "internalPort": 1502, "publicPort": None, "networkKey": "lab"},
        )
        if str(access.get("status", "")).lower() != "active":
            raise RuntimeError(f"服务开放失败: {access.get('lastError') or '未返回失败原因'}")
        host, port = access["endpoint"].rsplit(":", 1)
        deadline = time.monotonic() + 45
        last_error = None
        while time.monotonic() < deadline:
            try:
                self.probe_modbus(host, int(port))
                break
            except (OSError, RuntimeError) as exc:
                last_error = exc
                time.sleep(1)
        else:
            raise RuntimeError(f"Modbus 公网访问失败: {last_error}")
        self.event("probe", "Modbus 实际返回寄存器 12/34/56/78", endpoint=access["endpoint"])

        check = self.api("GET", f"/api/open/v1/teamlab/runtimes/{self.runtime_id}/status-check")
        differences = [item for item in check.get("items", []) if item.get("difference") != "matched"]
        if differences:
            raise RuntimeError(f"运行状态检查存在差异: {differences}")
        self.event("status", "平台状态与 Agent 现场状态一致")

        self.destroy(marker)
        self.event("complete", "短流程通过，运行资源已销毁")

    @staticmethod
    def probe_modbus(host: str, port: int) -> None:
        with socket.create_connection((host, port), timeout=5) as connection:
            request = struct.pack(">HHHBBHH", 1, 0, 6, 7, 3, 0, 4)
            connection.sendall(request)
            response = connection.recv(64)
        if len(response) < 17 or response[7] != 3 or response[8] != 8:
            raise RuntimeError("Modbus 响应格式错误")
        values = struct.unpack(">HHHH", response[9:17])
        if values != (12, 34, 56, 78):
            raise RuntimeError(f"Modbus 寄存器不匹配: {values}")

    def destroy(self, marker: str) -> None:
        if not self.runtime_id or self.runtime_destroyed:
            return
        submitted = self.api(
            "DELETE", f"/api/open/v1/teamlab/runtimes/{self.runtime_id}",
            key=f"preflight-destroy-{marker}", timeout=120,
        )
        self.wait_operation(submitted["id"], 1200)
        self.wait_runtime({"10", "destroyed"}, 600)
        self.runtime_destroyed = True

    def cleanup(self) -> None:
        cleanup_errors = []
        if self.runtime_id and not self.runtime_destroyed:
            try:
                self.destroy(datetime.now().strftime("%Y%m%d-%H%M%S"))
            except Exception as exc:
                cleanup_errors.append(f"runtime: {exc}")
        if self.release_id and self.token:
            for method, path in [
                ("DELETE", f"/api/open/v1/teamlab/preparations/releases/{self.release_id}"),
                ("POST", f"/api/open/v1/teamlab/topologies/{self.topology_id}/releases/{self.release_id}/archive"),
            ]:
                try:
                    self.api(method, path, key=f"preflight-cleanup-{self.release_id}")
                except Exception as exc:
                    cleanup_errors.append(f"{path}: {exc}")
        if self.scope_id and self.token:
            try:
                self.api("POST", f"/api/open/v1/teamlab/scopes/{self.scope_id}/archive")
            except Exception as exc:
                cleanup_errors.append(f"scope: {exc}")
        if self.token_id:
            response = self.admin.delete(self.base + f"/api/tokens/{self.token_id}", timeout=20)
            if response.status_code not in (204, 404):
                cleanup_errors.append(f"token: HTTP {response.status_code}")
        if cleanup_errors:
            self.event("cleanup", "；".join(cleanup_errors))

    def write_report(self, result: str, error: str | None) -> None:
        self.output.mkdir(parents=True, exist_ok=True)
        report = {
            "result": result, "completedAt": now(), "error": error,
            "scopeId": self.scope_id or None, "topologyId": self.topology_id or None,
            "releaseId": self.release_id or None, "runtimeId": self.runtime_id or None,
            "runtimeDestroyed": self.runtime_destroyed, "events": self.events,
        }
        (self.output / "preflight-report.json").write_text(
            json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8",
        )


def main() -> None:
    parser = argparse.ArgumentParser(description="TeamLab 比赛仿真短流程")
    parser.add_argument("--base-url", default="http://10.24.0.27:8080")
    parser.add_argument("--admin-user", default="admin")
    parser.add_argument("--output", default="artifacts/teamlab-match/preflight")
    args = parser.parse_args()
    password = getpass.getpass("平台管理员密码: ")
    run = ShortRun(args.base_url, args.admin_user, password, Path(args.output))
    password = ""
    error = None
    try:
        run.run()
    except Exception as exc:
        error = str(exc)
        run.event("failed", error)
        raise
    finally:
        run.cleanup()
        run.write_report("passed" if error is None else "failed", error)


if __name__ == "__main__":
    main()
