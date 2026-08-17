#!/usr/bin/env python3
"""TeamLab V1/V2 全链路性能流水线。

一键完成：
  1. 自动切换 118 主站/Agent 执行模型（V1 或 V2）
  2. 对指定拓扑 profile 执行 N 轮 create->ready->destroy（可选并发）
  3. 输出 JSON 原始报告 + 控制台数据分析（阶段耗时、均值、对比）

示例：
  GZCTF_API_TOKEN=... GZCTF_SSH_PASSWORD=... \\
  python scripts/validation/teamlab/run_teamlab_perf.py --mode v1 --profile docker
  python scripts/validation/teamlab/run_teamlab_perf.py --mode v2 --profile mixed --iterations 2
"""
import argparse
import json
import os
import sys
import time
import uuid

import paramiko
import requests

API_TOKEN = os.environ.get("GZCTF_API_TOKEN")
SSH_PASSWORD = os.environ.get("GZCTF_SSH_PASSWORD")

BASE = os.environ.get("GZCTF_BASE_URL", "http://10.0.7.118:8080")
API = BASE.rstrip("/") + "/api/open/v1"
HEADERS = {"Authorization": f"Bearer {API_TOKEN}"} if API_TOKEN else {}

RUNTIME_RUNNING = 5
RUNTIME_DESTROYED = 10
OP_SUCCEEDED = 2
OP_FAILED = {3, 4}

PROFILES = {
    "docker": {
        "topologyId": "01a00b7f-5c5c-7ea6-8040-2dac38550ed9",
        "releaseId": "01a00b7f-fbfd-789b-b6d4-60e436421999",
        "label": "Docker-only",
    },
    "mixed": {
        "topologyId": "019fe2f9-db22-71f2-9543-ea41b6d4c658",
        "releaseId": "019fe773-7565-7f3f-a250-ea0252d4ce02",
        "label": "Docker+LinuxVM+WindowsVM",
    },
}


def require(resp):
    if not resp.ok:
        raise RuntimeError(f"HTTP {resp.status_code}: {resp.text}")
    return resp.json()


def now():
    return time.monotonic()


def ssh_run(host, user, password, command, use_sudo=True):
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    client.connect(host, username=user, password=password, timeout=15,
                   look_for_keys=False, allow_agent=False)
    try:
        if use_sudo:
            remote = "sudo -S -p '' bash -c " + "'" + command.replace("'", "'\\''") + "'"
        else:
            remote = command
        stdin, stdout, stderr = client.exec_command(remote, timeout=600)
        if use_sudo:
            stdin.write(password + "\n")
        stdin.flush()
        stdin.channel.shutdown_write()
        out = stdout.read().decode("utf-8", "replace")
        err = stderr.read().decode("utf-8", "replace")
        code = stdout.channel.recv_exit_status()
        if code != 0:
            raise RuntimeError(f"SSH command failed ({code}): {err[-2000:]}\n{out[-2000:]}")
        return out
    finally:
        client.close()


def wait_http_200(host, timeout=90):
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            r = requests.get(f"http://{host}:8080/", timeout=3)
            if r.status_code == 200:
                return True
        except requests.RequestException:
            pass
        time.sleep(2)
    raise RuntimeError("service did not return HTTP 200 after restart")


def switch_execution_model(mode, host="10.0.7.118", user="whoami", password=None):
    password = password or SSH_PASSWORD
    if not password:
        raise RuntimeError("GZCTF_SSH_PASSWORD is required for automatic execution-model switching")
    mode = mode.upper()
    cmd = f"""
set -e
sudo python3 -c "import json; p='/opt/gzctf/publish/appsettings.json'; d=json.load(open(p)); d['TeamLabNetworkConfig']={{'ExecutionModel':'{mode}'}}; d['TeamLabNetwork']={{'ExecutionModel':'{mode}'}}; json.dump(d, open(p,'w'), indent=2, ensure_ascii=False)"
sudo sed -i 's/"ExecutionModel": "[^"]*"/"ExecutionModel": "{mode}"/g' /etc/gzctf-agent/appsettings.json
grep -n "ExecutionModel" /opt/gzctf/publish/appsettings.json /etc/gzctf-agent/appsettings.json
sudo systemctl restart gzctf.service gzctf-agent.service
"""
    ssh_run(host, user, password, cmd)
    wait_http_200(host)
    print(f"[switch] execution model -> {mode}")


def poll_operation(op_id, timeout=1800, label="operation"):
    deadline = now() + timeout
    last = None
    transitions = []
    start = now()
    while now() < deadline:
        op = require(requests.get(f"{API}/operations/{op_id}", headers=HEADERS, timeout=15))
        state = (op.get("status"), op.get("stage"), op.get("currentProgress"), op.get("totalProgress"))
        t = now() - start
        if state != last:
            transitions.append({"at": round(t, 3), "status": op.get("status"), "stage": op.get("stage"),
                                "currentProgress": op.get("currentProgress"), "totalProgress": op.get("totalProgress")})
            last = state
        if op.get("status") == OP_SUCCEEDED:
            return op, transitions
        if op.get("status") in OP_FAILED:
            raise RuntimeError(f"operation {op_id} failed: {json.dumps(op, ensure_ascii=False)}")
        time.sleep(0.2)
    raise TimeoutError(f"operation {op_id} timeout")


def poll_runtime(runtime_id, expected_status, timeout=1800, label="runtime"):
    deadline = now() + timeout
    last = None
    transitions = []
    start = now()
    while now() < deadline:
        rt = require(requests.get(f"{API}/teamlab/runtimes/{runtime_id}", headers=HEADERS, timeout=15))
        state = (rt.get("status"), rt.get("stage"), rt.get("generation"))
        t = now() - start
        if state != last:
            transitions.append({"at": round(t, 3), "status": rt.get("status"), "stage": rt.get("stage"),
                                "generation": rt.get("generation")})
            last = state
        if rt.get("status") in (6, 7):
            raise RuntimeError(f"runtime {runtime_id} failed: {json.dumps(rt, ensure_ascii=False)}")
        if rt.get("status") == expected_status and (rt.get("generation") or 0) >= 1:
            return rt, transitions
        time.sleep(0.2)
    raise TimeoutError(f"runtime {runtime_id} timeout")


def create_runtime(release_id, external_ref, key):
    body = {"releaseId": release_id, "externalReference": external_ref, "overlays": []}
    t0 = now()
    op = require(requests.post(f"{API}/teamlab/runtimes",
        headers={**HEADERS, "Idempotency-Key": key, "Content-Type": "application/json"}, json=body, timeout=30))
    accepted_ms = (now() - t0) * 1000
    op_id = op["id"]
    completed, op_trans = poll_operation(op_id, label="create")
    runtime_id = completed.get("resourceId") or op.get("resourceId")
    runtime, rt_trans = poll_runtime(runtime_id, RUNTIME_RUNNING)
    return {"operationId": op_id, "runtimeId": runtime_id, "acceptedMs": accepted_ms,
            "operationMs": op_trans[-1]["at"] * 1000 if op_trans else None,
            "totalMs": (now() - t0) * 1000,
            "operationTransitions": op_trans, "runtimeTransitions": rt_trans, "runtime": runtime}


def destroy_runtime(runtime_id, key):
    t0 = now()
    op = require(requests.delete(f"{API}/teamlab/runtimes/{runtime_id}",
        headers={**HEADERS, "Idempotency-Key": key}, timeout=30))
    accepted_ms = (now() - t0) * 1000
    op_id = op["id"]
    completed, op_trans = poll_operation(op_id, label="destroy")
    runtime, rt_trans = poll_runtime(runtime_id, RUNTIME_DESTROYED, timeout=900)
    return {"operationId": op_id, "acceptedMs": accepted_ms,
            "operationMs": op_trans[-1]["at"] * 1000 if op_trans else None,
            "totalMs": (now() - t0) * 1000,
            "operationTransitions": op_trans, "runtimeTransitions": rt_trans, "runtime": runtime}


def create_plan(topology_id, release_id):
    t0 = now()
    plan = require(requests.post(f"{API}/teamlab/topologies/{topology_id}/releases/{release_id}/plan",
        headers=HEADERS, timeout=60))
    return {"totalMs": (now() - t0) * 1000, "shards": len(plan.get("shards", [])), "plan": plan}


def analyze(report):
    print("\n===== 性能分析 =====")
    print(f"mode={report.get('mode')} profile={report.get('profile')}")
    creates = [s for s in report["steps"] if s.get("stage", "").endswith("-create")]
    destroys = [s for s in report["steps"] if s.get("stage", "").endswith("-destroy")]
    if creates:
        vals = [s["operationMs"] for s in creates if s.get("operationMs")]
        print(f"Create operation ms: {[round(v, 1) for v in vals]} avg={round(sum(vals)/len(vals), 1) if vals else 'n/a'}")
    if destroys:
        vals = [s["operationMs"] for s in destroys if s.get("operationMs")]
        print(f"Destroy operation ms: {[round(v, 1) for v in vals]} avg={round(sum(vals)/len(vals), 1) if vals else 'n/a'}")
    for s in report["steps"]:
        trans = s.get("operationTransitions")
        if not trans:
            continue
        print(f"\n-- {s['stage']} --")
        prev = 0.0
        for t in trans:
            print(f"  {t['stage']:<22} @{t['at']*1000:8.1f}ms dur={(t['at']-prev)*1000:8.1f}ms status={t['status']}")
            prev = t['at']
    cb = report.get("concurrentBatch")
    if cb:
        print(f"\nConcurrent batch totalMs={cb['totalMs']:.1f}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mode", choices=["v1", "v2"], required=True)
    parser.add_argument("--profile", choices=list(PROFILES), default="docker")
    parser.add_argument("--topology-id", help="override profile topology id")
    parser.add_argument("--release-id", help="override profile release id")
    parser.add_argument("--iterations", type=int, default=3)
    parser.add_argument("--concurrency", type=int, default=1)
    parser.add_argument("--out", default=None)
    parser.add_argument("--ssh-host", default="10.0.7.118")
    parser.add_argument("--ssh-user", default="whoami")
    parser.add_argument("--restore-v2", action="store_true", default=True,
                        help="after V1 run, restore V2 config")
    parser.add_argument("--no-restore-v2", dest="restore_v2", action="store_false")
    args = parser.parse_args()

    if not API_TOKEN:
        parser.error("GZCTF_API_TOKEN is required")
    prof = PROFILES[args.profile]
    topology_id = args.topology_id or prof["topologyId"]
    release_id = args.release_id or prof["releaseId"]
    run_id = uuid.uuid4().hex[:12]
    out_path = args.out or f"artifacts/perf-teamlab/{args.mode}-{args.profile}-{time.strftime('%Y%m%d-%H%M%S')}.json"
    os.makedirs(os.path.dirname(out_path), exist_ok=True)

    report = {"mode": args.mode, "profile": args.profile, "topologyId": topology_id,
              "releaseId": release_id, "runId": run_id, "startedAt": time.time(), "steps": []}

    try:
        if args.mode == "v1" or args.mode == "v2":
            switch_execution_model(args.mode, args.ssh_host, args.ssh_user, SSH_PASSWORD)

        plan = create_plan(topology_id, release_id)
        report["plan"] = plan
        report["steps"].append({"stage": "plan", **plan})
        print("plan", json.dumps(plan, ensure_ascii=False, default=str))

        for i in range(1, args.iterations + 1):
            ext = f"perf-{args.mode}-{run_id}-seq-{i}"
            create = create_runtime(release_id, ext, f"perf-{args.mode}-{run_id}-create-{i}")
            print("create", i, {k: v for k, v in create.items() if k not in ("operationTransitions", "runtimeTransitions", "runtime")})
            destroy = destroy_runtime(create["runtimeId"], f"perf-{args.mode}-{run_id}-destroy-{i}")
            print("destroy", i, {k: v for k, v in destroy.items() if k not in ("operationTransitions", "runtimeTransitions", "runtime")})
            report["steps"].append({"stage": f"cycle-{i}-create", **create})
            report["steps"].append({"stage": f"cycle-{i}-destroy", **destroy})

        if args.concurrency > 1:
            items = []
            t0 = now()
            for i in range(1, args.concurrency + 1):
                ext = f"perf-{args.mode}-{run_id}-conc-{i}"
                key = f"perf-{args.mode}-{run_id}-ccreate-{i}"
                body = {"releaseId": release_id, "externalReference": ext, "overlays": []}
                op = require(requests.post(f"{API}/teamlab/runtimes", headers={**HEADERS, "Idempotency-Key": key, "Content-Type": "application/json"}, json=body, timeout=30))
                items.append({"operationId": op["id"], "acceptedAt": now() - t0})
            for item in items:
                completed, op_trans = poll_operation(item["operationId"])
                runtime_id = completed.get("resourceId")
                runtime, rt_trans = poll_runtime(runtime_id, RUNTIME_RUNNING)
                item["runtimeId"] = runtime_id
                item["operationTransitions"] = op_trans
                item["runtimeTransitions"] = rt_trans
                item["runtime"] = runtime
            for item in items:
                destroy = destroy_runtime(item["runtimeId"], f"perf-{args.mode}-{run_id}-cdestroy-{item['runtimeId'][:8]}")
                item["destroy"] = destroy
            report["concurrentBatch"] = {"count": args.concurrency, "totalMs": (now() - t0) * 1000, "items": items}
            report["steps"].append({"stage": f"concurrent-{args.concurrency}-create-destroy", "totalMs": (now() - t0) * 1000, "items": items})
    finally:
        if args.mode == "v1" and args.restore_v2:
            print("[restore] switching back to V2")
            switch_execution_model("v2", args.ssh_host, args.ssh_user, SSH_PASSWORD)

    report["completedAt"] = time.time()
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=2)
    print(f"\nreport saved: {out_path}")
    analyze(report)


if __name__ == "__main__":
    main()
