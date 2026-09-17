#!/usr/bin/env python3
"""TeamLab 双队比赛环境仿真器。只通过公开/管理 API 和真实网络探测工作。"""

from __future__ import annotations

import argparse
import asyncio
import json
import os
import re
import struct
import time
from collections import defaultdict, deque
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib.parse import urljoin

from aiohttp import ClientError, ClientSession, ClientTimeout, web


def utcnow() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def percentile(values: list[float], fraction: float) -> float:
    if not values:
        return 0.0
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, int((len(ordered) - 1) * fraction))]


ENV = re.compile(r"\$\{([A-Z][A-Z0-9_]*)\}")


def expand(value: Any) -> Any:
    if isinstance(value, str):
        missing = [name for name in ENV.findall(value) if not os.getenv(name)]
        if missing:
            raise ValueError(f"缺少环境变量: {', '.join(sorted(set(missing)))}")
        return ENV.sub(lambda match: os.environ[match.group(1)], value)
    if isinstance(value, list):
        return [expand(item) for item in value]
    if isinstance(value, dict):
        return {key: expand(item) for key, item in value.items()}
    return value


@dataclass
class Metric:
    at: float
    source: str
    ok: bool
    latency_ms: float
    status: int | None = None


@dataclass
class State:
    name: str
    duration: int
    started: float = field(default_factory=time.monotonic)
    phase: str = "启动"
    finished: bool = False
    metrics: deque[Metric] = field(default_factory=lambda: deque(maxlen=200_000))
    errors: dict[str, dict[str, Any]] = field(default_factory=dict)
    monitors: dict[str, Any] = field(default_factory=dict)
    stages: list[dict[str, Any]] = field(default_factory=list)
    subscribers: set[asyncio.Queue[str]] = field(default_factory=set)

    def record(self, metric: Metric) -> None:
        self.metrics.append(metric)

    def failure(self, category: str, source: str, detail: str) -> None:
        detail = detail[:500]
        key = f"{category}|{source}|{detail}"
        item = self.errors.get(key)
        if item:
            item["count"] += 1
            item["lastAt"] = utcnow()
        else:
            self.errors[key] = {
                "category": category, "source": source, "detail": detail,
                "count": 1, "firstAt": utcnow(), "lastAt": utcnow(),
            }

    def snapshot(self) -> dict[str, Any]:
        elapsed = max(0.0, time.monotonic() - self.started)
        recent = [m for m in self.metrics if m.at >= time.monotonic() - 5]
        all_latency = [m.latency_ms for m in self.metrics]
        teams: dict[str, Any] = {}
        for source in sorted({m.source.split("/", 1)[0] for m in self.metrics if "/" in m.source}):
            selected = [m for m in self.metrics if m.source.startswith(source + "/")]
            teams[source] = {
                "success": sum(m.ok for m in selected), "failed": sum(not m.ok for m in selected),
                "p95Ms": percentile([m.latency_ms for m in selected], .95),
            }
        return {
            "name": self.name, "phase": self.phase, "elapsedSeconds": elapsed,
            "progressPercent": min(100, elapsed / max(1, self.duration) * 100),
            "finished": self.finished,
            "metrics": {
                "total": len(self.metrics), "success": sum(m.ok for m in self.metrics),
                "failed": sum(not m.ok for m in self.metrics), "rps": len(recent) / 5,
                "p50Ms": percentile(all_latency, .5), "p95Ms": percentile(all_latency, .95),
                "p99Ms": percentile(all_latency, .99),
            },
            "teams": teams, "errors": list(self.errors.values()),
            "monitors": self.monitors, "stages": self.stages,
        }

    async def publish(self) -> None:
        payload = json.dumps(self.snapshot(), ensure_ascii=False)
        for queue in tuple(self.subscribers):
            if queue.full():
                try:
                    queue.get_nowait()
                except asyncio.QueueEmpty:
                    pass
            queue.put_nowait(payload)


class Platform:
    def __init__(self, base_url: str, token: str, state: State):
        self.base_url = base_url.rstrip("/") + "/"
        self.state = state
        headers = {"Authorization": f"Bearer {token}"} if token else {}
        self.session = ClientSession(headers=headers, timeout=ClientTimeout(total=30))

    async def close(self) -> None:
        await self.session.close()

    async def request(self, method: str, path: str, *, source: str, body: Any = None) -> Any:
        started = time.monotonic()
        status = None
        try:
            async with self.session.request(method, urljoin(self.base_url, path.lstrip("/")), json=body) as response:
                status = response.status
                raw = await response.text()
                if response.status >= 400:
                    raise RuntimeError(f"HTTP {response.status}")
                result = json.loads(raw) if raw and "json" in response.headers.get("Content-Type", "") else raw
                self.state.record(Metric(time.monotonic(), source, True, (time.monotonic()-started)*1000, status))
                return result
        except (ClientError, asyncio.TimeoutError, RuntimeError) as exc:
            self.state.record(Metric(time.monotonic(), source, False, (time.monotonic()-started)*1000, status))
            self.state.failure("api", source, str(exc))
            raise


async def http_probe(session: ClientSession, probe: dict[str, Any]) -> None:
    async with session.get(probe["url"], timeout=ClientTimeout(total=10)) as response:
        await response.read()
        expected = int(probe.get("expectStatus", 200))
        if response.status != expected:
            raise RuntimeError(f"HTTP {response.status}，期望 {expected}")


async def tcp_probe(probe: dict[str, Any]) -> None:
    reader, writer = await asyncio.wait_for(asyncio.open_connection(probe["host"], int(probe["port"])), 5)
    writer.close()
    await writer.wait_closed()


async def modbus_probe(probe: dict[str, Any]) -> None:
    reader, writer = await asyncio.wait_for(asyncio.open_connection(probe["host"], int(probe["port"])), 5)
    try:
        transaction = int(time.monotonic_ns() & 0xFFFF)
        pdu = struct.pack(">BHH", 3, int(probe.get("startAddress", 0)), int(probe.get("quantity", 1)))
        writer.write(struct.pack(">HHHB", transaction, 0, len(pdu) + 1, int(probe.get("unitId", 1))) + pdu)
        await writer.drain()
        header = await asyncio.wait_for(reader.readexactly(7), 5)
        rx_transaction, protocol, length, _ = struct.unpack(">HHHB", header)
        payload = await asyncio.wait_for(reader.readexactly(length - 1), 5)
        if rx_transaction != transaction or protocol != 0 or not payload or payload[0] != 3:
            raise RuntimeError("Modbus 响应与请求不匹配")
    finally:
        writer.close()
        await writer.wait_closed()


async def player(platform: Platform, state: State, team: dict[str, Any], index: int, stop: asyncio.Event) -> None:
    probes = team.get("probes", [])
    async with ClientSession() as session:
        turn = index
        while not stop.is_set():
            elapsed = time.monotonic() - state.started
            steady = int(team.get("virtualUsers", 1))
            burst = int(team.get("burstUsers", steady))
            active = burst if elapsed % 900 < 60 else steady
            if index >= active:
                try:
                    await asyncio.wait_for(stop.wait(), timeout=1)
                except asyncio.TimeoutError:
                    pass
                continue
            probe = probes[turn % len(probes)]
            source = f"{team['name']}/{probe['name']}"
            started = time.monotonic()
            try:
                if probe["type"] == "http":
                    await http_probe(session, probe)
                elif probe["type"] == "tcp":
                    await tcp_probe(probe)
                elif probe["type"] == "modbus":
                    await modbus_probe(probe)
                elif probe["type"] == "api":
                    await platform.request(
                        probe.get("method", "GET"), probe["path"],
                        source=source, body=probe.get("body"),
                    )
                else:
                    raise RuntimeError(f"未知探测类型 {probe['type']}")
                state.record(Metric(time.monotonic(), source, True, (time.monotonic()-started)*1000))
            except Exception as exc:
                state.record(Metric(time.monotonic(), source, False, (time.monotonic()-started)*1000))
                state.failure("asset_access", source, str(exc))
            turn += 1
            try:
                await asyncio.wait_for(stop.wait(), timeout=1 + (index % 3))
            except asyncio.TimeoutError:
                pass


async def monitor(platform: Platform, state: State, item: dict[str, Any], stop: asyncio.Event) -> None:
    while not stop.is_set():
        try:
            value = await platform.request("GET", item["path"], source=f"monitor:{item['name']}")
            selected = item.get("select", [])
            if selected and isinstance(value, dict):
                value = {key: value.get(key) for key in selected}
            elif not selected:
                value = {"items": len(value)} if isinstance(value, list) else {"status": "sampled"}
            state.monitors[item["name"]] = value
        except Exception:
            state.monitors[item["name"]] = {"status": "failed", "at": utcnow()}
        try:
            await asyncio.wait_for(stop.wait(), timeout=float(item.get("intervalSeconds", 5)))
        except asyncio.TimeoutError:
            pass


async def execute_stage(platform: Platform, state: State, stage: dict[str, Any]) -> None:
    result = next(item for item in state.stages if item["name"] == stage["name"])
    state.phase = stage["name"]
    result["status"] = "running"
    result["startedAt"] = utcnow()
    try:
        for action in stage.get("actions", []):
            await platform.request(action.get("method", "GET"), action["path"], source=f"stage:{stage['name']}:{action['name']}", body=action.get("body"))
        result["status"] = "passed"
    except Exception as exc:
        result["status"] = "failed"
        result["error"] = str(exc)[:500]
    result["completedAt"] = utcnow()


async def orchestrate(config: dict[str, Any], state: State, output: Path) -> None:
    token = config.get("auth", {}).get("bearerToken", "")
    platform = Platform(config["baseUrl"], token, state)
    stop = asyncio.Event()
    tasks: list[asyncio.Task[Any]] = []
    try:
        for team in config.get("teams", []):
            if not team.get("probes"):
                raise ValueError(f"队伍 {team['name']} 没有真实探测目标")
            workers = max(int(team.get("virtualUsers", 1)), int(team.get("burstUsers", 1)))
            tasks.extend(asyncio.create_task(player(platform, state, team, i, stop)) for i in range(workers))
        tasks.extend(asyncio.create_task(monitor(platform, state, item, stop)) for item in config.get("monitors", []))
        stages = sorted(config.get("stages", []), key=lambda item: item.get("atSeconds", 0))
        for stage in stages:
            delay = state.started + float(stage.get("atSeconds", 0)) - time.monotonic()
            if delay > 0:
                await asyncio.sleep(delay)
            if time.monotonic() - state.started >= state.duration:
                break
            await execute_stage(platform, state, stage)
        remaining = state.started + state.duration - time.monotonic()
        if remaining > 0:
            await asyncio.sleep(remaining)
    finally:
        stop.set()
        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)
        await platform.close()
        state.finished = True
        state.phase = "已结束"
        output.mkdir(parents=True, exist_ok=True)
        snapshot = state.snapshot()
        (output / "report.json").write_text(json.dumps(snapshot, ensure_ascii=False, indent=2), encoding="utf-8")
        dashboard = (Path(__file__).parent / "web" / "index.html").read_text(encoding="utf-8")
        embedded = dashboard.replace("fetch('/api/state').then(r=>r.json()).then(draw);", f"draw({json.dumps(snapshot, ensure_ascii=False)});")
        (output / "report.html").write_text(embedded, encoding="utf-8")
        await state.publish()


async def serve(state: State, listen: str) -> web.AppRunner:
    async def index(_: web.Request) -> web.FileResponse:
        return web.FileResponse(Path(__file__).parent / "web" / "index.html")

    async def current(_: web.Request) -> web.Response:
        return web.json_response(state.snapshot())

    async def events(_: web.Request) -> web.StreamResponse:
        response = web.StreamResponse(headers={"Content-Type": "text/event-stream", "Cache-Control": "no-cache"})
        await response.prepare(_)
        queue: asyncio.Queue[str] = asyncio.Queue(maxsize=2)
        state.subscribers.add(queue)
        try:
            while True:
                payload = await queue.get()
                await response.write(f"data: {payload}\n\n".encode())
        except (ConnectionResetError, asyncio.CancelledError):
            return response
        finally:
            state.subscribers.discard(queue)

    app = web.Application()
    app.add_routes([web.get("/", index), web.get("/api/state", current), web.get("/events", events)])
    runner = web.AppRunner(app)
    await runner.setup()
    host, port = listen.rsplit(":", 1)
    await web.TCPSite(runner, host, int(port)).start()
    return runner


async def main_async(args: argparse.Namespace) -> None:
    config = expand(json.loads(Path(args.scenario).read_text(encoding="utf-8")))
    duration = args.duration or int(config.get("durationSeconds", 7200))
    state = State(config.get("name", "TeamLab 比赛仿真"), duration)
    state.stages = [{"name": item["name"], "status": "pending"} for item in config.get("stages", [])]
    server = await serve(state, args.listen)
    publisher = asyncio.create_task(periodic_publish(state))
    print(f"看板: http://{args.listen}/")
    try:
        await orchestrate(config, state, Path(args.output))
    finally:
        publisher.cancel()
        await asyncio.gather(publisher, return_exceptions=True)
        await server.cleanup()


async def periodic_publish(state: State) -> None:
    while True:
        await state.publish()
        await asyncio.sleep(1)


def main() -> None:
    parser = argparse.ArgumentParser(description="TeamLab 双队比赛环境仿真器")
    parser.add_argument("--scenario", required=True)
    parser.add_argument("--listen", default="127.0.0.1:19090")
    parser.add_argument("--duration", type=int, help="覆盖测试时长，便于短流程检查")
    parser.add_argument("--output", default="artifacts/teamlab-match/latest")
    args = parser.parse_args()
    asyncio.run(main_async(args))


if __name__ == "__main__":
    main()
