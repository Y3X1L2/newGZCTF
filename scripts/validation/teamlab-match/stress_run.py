#!/usr/bin/env python3
"""Run the focused TeamLab data-plane and large-transfer stress test."""

from __future__ import annotations

import argparse
import asyncio
import hashlib
import json
import os
import random
import time
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import urlsplit
import requests
from aiohttp import ClientSession, ClientTimeout, TCPConnector

from long_run import Match


SCAN_PATHS = (
    "/", "/admin", "/login", "/api", "/api/v1", "/.git/config", "/.env",
    "/phpmyadmin", "/actuator/health", "/server-status", "/backup.zip",
    "/robots.txt", "/sitemap.xml", "/wp-admin", "/console",
)


def utcnow() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def percentile(values: list[float], fraction: float) -> float:
    if not values:
        return 0.0
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int((len(ordered) - 1) * fraction))]


class StressRun:
    def __init__(self, base_url: str, token: str, output: Path, file_size_mib: int, data_plane_host: str):
        self.base = base_url.rstrip("/")
        self.token = token
        self.output = output
        self.file_size = file_size_mib * 1024 * 1024
        self.data_plane_host = data_plane_host
        self.match = Match(self.base, token, output)
        self.report: dict = {
            "name": "TeamLab Docker/VM high-concurrency stress",
            "startedAt": utcnow(),
            "status": "running",
            "environment": {"teams": 2, "assetsPerTeam": 20},
            "phases": [],
            "largeTransfers": [],
            "errors": [],
        }

    def save(self) -> None:
        self.output.mkdir(parents=True, exist_ok=True)
        temporary = self.output / "stress-report.json.tmp"
        temporary.write_text(json.dumps(self.report, ensure_ascii=False, indent=2), encoding="utf-8")
        temporary.replace(self.output / "stress-report.json")

    def prepare_team(self, resource: dict) -> dict:
        runtime = self.match.request("GET", f"/api/open/v1/teamlab/runtimes/{resource['runtimeId']}")
        assets = {asset["key"]: asset for asset in runtime["assets"]}
        resource.update({
            "dockerAssetId": assets["service-1"]["id"],
            "linuxVmId": assets["linux-vm"]["id"],
            "windowsVmId": assets["windows-vm"]["id"],
        })
        return resource

    def transfer_file(self, resource: dict, asset_id: int, kind: str, payload: Path, digest: str) -> dict:
        path = f"/tmp/yinyu-stress-{self.match.marker}.bin"
        base = f"{self.base}/api/open/v1/teamlab/runtimes/{resource['runtimeId']}/assets/{asset_id}/files"
        session = requests.Session()
        session.headers["Authorization"] = f"Bearer {self.token}"
        params = {"generation": resource["generation"], "path": path, "overwrite": "true", "confirmed": "true"}
        started = time.monotonic()
        with payload.open("rb") as source:
            response = session.post(base + "/upload", params=params, data=source, headers={
                "Content-Type": "application/octet-stream", "Content-Length": str(self.file_size),
            }, timeout=900)
        response.raise_for_status()
        upload_seconds = time.monotonic() - started

        downloaded = hashlib.sha256()
        downloaded_bytes = 0
        started = time.monotonic()
        with session.get(base + "/download", params={"generation": resource["generation"], "path": path},
                         stream=True, timeout=900) as response:
            response.raise_for_status()
            for block in response.iter_content(1024 * 1024):
                downloaded.update(block)
                downloaded_bytes += len(block)
        download_seconds = time.monotonic() - started
        if downloaded_bytes != self.file_size or downloaded.hexdigest() != digest:
            raise RuntimeError(f"{kind} download digest mismatch")
        response = session.delete(base, params={
            "generation": resource["generation"], "path": path,
            "recursive": "false", "confirmed": "true",
        }, timeout=120)
        response.raise_for_status()
        return {
            "team": resource["team"], "kind": kind, "bytes": self.file_size,
            "uploadSeconds": upload_seconds,
            "uploadMiBps": self.file_size / 1024 / 1024 / upload_seconds,
            "downloadSeconds": download_seconds,
            "downloadMiBps": self.file_size / 1024 / 1024 / download_seconds,
            "sha256": digest,
        }

    def large_transfers(self, teams: list[dict]) -> list[dict]:
        payload = self.output / "payload.bin"
        block = hashlib.sha256(self.match.marker.encode()).digest() * 32768
        with payload.open("wb") as target:
            for _ in range(self.file_size // len(block)):
                target.write(block)
            target.write(block[:self.file_size % len(block)])
        digest = hashlib.sha256(payload.read_bytes()).hexdigest()
        jobs = []
        with ThreadPoolExecutor(max_workers=4) as pool:
            for resource in teams:
                jobs.append(pool.submit(self.transfer_file, resource, resource["dockerAssetId"], "docker", payload, digest))
                jobs.append(pool.submit(self.transfer_file, resource, resource["linuxVmId"], "linux-vm", payload, digest))
            results = []
            for job in jobs:
                try:
                    results.append(job.result())
                except Exception as error:
                    results.append({"status": "failed", "error": str(error)})
        payload.unlink(missing_ok=True)
        return results

    async def request_once(self, session: ClientSession, target: dict, sequence: int) -> tuple[bool, float, str, int]:
        started = time.monotonic()
        try:
            path = SCAN_PATHS[sequence % len(SCAN_PATHS)]
            suffix = f"?scan={sequence}" if "?" not in path else f"&scan={sequence}"
            async with session.get(target["url"] + path.lstrip("/") + suffix) as response:
                body = await response.read()
                return response.status < 500, (time.monotonic() - started) * 1000, f"http:{response.status}", len(body)
        except Exception as error:
            return (False, (time.monotonic() - started) * 1000,
                    f"http:{type(error).__name__}", 0)

    async def phase(self, session: ClientSession, targets: list[dict], rate: int, seconds: int) -> dict:
        queue: asyncio.Queue[int] = asyncio.Queue(maxsize=max(rate * 2, 2000))
        latencies: list[float] = []
        outcomes: Counter[str] = Counter()
        transferred = 0
        rng = random.Random(rate)
        weighted = [target for target in targets for _ in range(target["weight"])]

        async def worker() -> None:
            nonlocal transferred
            while True:
                sequence = await queue.get()
                try:
                    target = weighted[rng.randrange(len(weighted))]
                    ok, latency, outcome, size = await self.request_once(session, target, sequence)
                    latencies.append(latency)
                    outcomes[("ok:" if ok else "failed:") + outcome] += 1
                    transferred += size
                finally:
                    queue.task_done()

        worker_count = max(100, rate * 4)
        workers = [asyncio.create_task(worker()) for _ in range(worker_count)]
        planned = rate * seconds
        submitted = 0
        started = time.monotonic()
        for sequence in range(planned):
            due = started + sequence / rate
            delay = due - time.monotonic()
            if delay > 0:
                await asyncio.sleep(delay)
            try:
                queue.put_nowait(sequence)
                submitted += 1
            except asyncio.QueueFull:
                pass
        remaining = started + seconds - time.monotonic()
        if remaining > 0:
            await asyncio.sleep(remaining)
        elapsed = time.monotonic() - started
        for worker_task in workers:
            worker_task.cancel()
        await asyncio.gather(*workers, return_exceptions=True)
        completed = len(latencies)
        succeeded = sum(value for key, value in outcomes.items() if key.startswith("ok:"))
        return {
            "targetRps": rate, "scheduledSeconds": seconds, "elapsedSeconds": elapsed,
            "plannedRequests": planned, "submittedRequests": submitted,
            "requests": completed, "clientDroppedRequests": planned - submitted,
            "unfinishedRequests": submitted - completed,
            "actualRps": completed / elapsed, "success": succeeded,
            "failed": completed - succeeded,
            "successRate": succeeded / completed * 100 if completed else 0,
            "p50Ms": percentile(latencies, .50), "p95Ms": percentile(latencies, .95),
            "p99Ms": percentile(latencies, .99), "responseBytes": transferred,
            "outcomes": dict(outcomes),
        }

    async def stress(self, teams: list[dict], phases: list[tuple[int, int]]) -> None:
        targets = []
        for resource in teams:
            endpoint = urlsplit(resource["web"] if "://" in resource["web"] else "tcp://" + resource["web"])
            web = f"http://{self.data_plane_host}:{endpoint.port}"
            targets.append({"url": web.rstrip("/") + "/", "weight": 1})
        connector = TCPConnector(limit=0, ttl_dns_cache=300, keepalive_timeout=30)
        timeout = ClientTimeout(total=5)
        transfer_task = asyncio.create_task(asyncio.to_thread(self.large_transfers, teams))
        async with ClientSession(connector=connector, timeout=timeout) as session:
            for rate, seconds in phases:
                phase = await self.phase(session, targets, rate, seconds)
                self.report["phases"].append(phase)
                self.save()
                print(f"rate={rate} actual={phase['actualRps']:.1f} success={phase['successRate']:.3f}% ",
                      f"p95={phase['p95Ms']:.1f}ms", flush=True)
        self.report["largeTransfers"] = await transfer_task

    async def run(self, phases: list[tuple[int, int]]) -> None:
        final_status = "completed"
        error = None
        try:
            scope = self.match.request("POST", "/api/open/v1/teamlab/scopes", {
                "key": f"stress-{self.match.marker.lower()}",
                "displayName": f"Docker VM stress {self.match.marker}",
            })
            self.match.scope_id = scope["id"]
            red = self.prepare_team(await asyncio.to_thread(self.match.provision, "red", 180))
            blue = self.prepare_team(await asyncio.to_thread(self.match.provision, "blue", 190))
            self.report["resources"] = [
                {key: value for key, value in item.items() if key not in {"topologyId", "releaseId"}}
                for item in (red, blue)
            ]
            self.save()
            await self.stress([red, blue], phases)
            for resource in (red, blue):
                check = self.match.request("GET", f"/api/open/v1/teamlab/runtimes/{resource['runtimeId']}/status-check")
                self.report.setdefault("statusChecks", []).append({"team": resource["team"], "result": check})
        except Exception as exception:
            final_status = "failed"
            error = str(exception)
            self.report["errors"].append(error)
            raise
        finally:
            await asyncio.to_thread(self.match.cleanup, final_status, error)
            self.report["status"] = final_status
            self.report["finishedAt"] = utcnow()
            self.report["cleanup"] = json.loads((self.output / "lifecycle.json").read_text(encoding="utf-8"))
            self.save()


def parse_phases(value: str) -> list[tuple[int, int]]:
    phases = []
    for item in value.split(","):
        rate, seconds = item.split("x", 1)
        phases.append((int(rate), int(seconds)))
    return phases


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://10.24.0.27:8080")
    parser.add_argument("--output", default="artifacts/teamlab-match/stress-latest")
    parser.add_argument("--phases", default="500x30,1000x30,1500x30,2000x210")
    parser.add_argument("--file-size-mib", type=int, default=64)
    parser.add_argument("--data-plane-host", required=True)
    args = parser.parse_args()
    token = os.environ.get("GZCTF_API_TOKEN", "")
    if not token:
        raise RuntimeError("GZCTF_API_TOKEN is required")
    test = StressRun(args.base_url, token, Path(args.output), args.file_size_mib, args.data_plane_host)
    asyncio.run(test.run(parse_phases(args.phases)))


if __name__ == "__main__":
    main()
